using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using AiTranslator.Core.Abstractions;

namespace AiTranslator.Infrastructure.Awareness;

/// <summary>
/// Classifies and locates the focused element of a window via UI Automation (M2 Task 3, ADR-0003).
/// Uses the managed <see cref="AutomationElement"/> client, which ships in the WindowsDesktop
/// framework (no extra package — important for the offline build) and is understood by Win32, WPF,
/// and modern Chromium/WebView2/Electron (which expose a UIA provider once a UIA client is active).
/// </summary>
/// <remarks>
/// Scope note: this resolves the focused element's <see cref="AutomationElement.Current"/> bounding
/// rectangle (physical pixels). Caret-precise placement (TextPattern2 / GetGUIThreadInfo) is deferred
/// until manual per-app testing shows it is needed — modern UIA covers the common cases.
///
/// WebView2 wake (the WhatsApp case): a Chromium renderer builds its accessibility tree lazily, only
/// once an MSAA/UIA client asks for it. Until then <see cref="AutomationElement.FocusedElement"/>
/// returns the renderer's root pane (a full-window, non-editable element) instead of the focused
/// <c>contenteditable</c>. We detect that and "wake" the renderer with a one-shot
/// <c>AccessibleObjectFromWindow(OBJID_CLIENT)</c> (the standard handshake screen readers use), then
/// re-query the focused element. The wake is per-HWND and cached so we pay it once per renderer.
/// </remarks>
public sealed class TargetResolver : ITargetResolver
{
    private const uint OBJID_CLIENT = 0xFFFFFFFC;
    private const int WakeRetries = 5;          // poll the focused element after waking the renderer
    private const int WakeRetryDelayMs = 60;    // ~300ms total — bounded by FocusWatcher's resolve timeout

    private static readonly Guid IID_IAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly HashSet<nint> _wokenRenderers = new();
    private readonly object _wokenLock = new();

    public FieldLocation? Resolve(nint windowHandle)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                Log("focusedElement: <null>");
                return null;
            }

            // Never treat our own floating box as a target (it has focus while the user types) —
            // that would mis-anchor the badge.
            if (focused.Current.ProcessId == _ownProcessId)
            {
                return null;
            }

            if (IsEditable(focused))
            {
                return Describe(focused, "direct");
            }

            // Non-editable focused element. In a Chromium/WebView2 host (WhatsApp, …) this is the
            // un-woken renderer root: wake it and re-query for the real contenteditable.
            var woken = TryWakeAndResolve(focused, windowHandle);
            if (woken is not null)
            {
                return woken;
            }

            return Describe(focused, "non-editable");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Any UIA/COM failure (element vanished, provider mid-teardown, ArgumentException/
            // Win32Exception from a flaky Chromium/Electron provider, …) is "unresolved", never a
            // crash — this runs on a thread whose unhandled exception would kill the process.
            return null;
        }
    }

    /// <summary>
    /// Wake the renderer that owns the (non-editable) focused element, then poll for the focused
    /// editable element the now-built accessibility tree exposes. Returns null if nothing editable
    /// surfaces within the budget — a later focus/location event re-resolves once the tree is ready.
    /// </summary>
    private FieldLocation? TryWakeAndResolve(AutomationElement root, nint foreground)
    {
        nint rootHwnd = SafeNativeWindowHandle(root);
        nint primary = rootHwnd != 0 ? rootHwnd : foreground;

        // Already woken this renderer and the focus is still non-editable → it is genuinely a
        // non-editable element (a button, the page body). Don't burn the retry budget re-polling.
        lock (_wokenLock)
        {
            if (primary != 0 && _wokenRenderers.Contains(primary))
            {
                return null;
            }
        }

        WakeAccessibility(rootHwnd);
        if (foreground != 0 && foreground != rootHwnd)
        {
            WakeAccessibility(foreground);
        }

        lock (_wokenLock)
        {
            if (primary != 0)
            {
                if (_wokenRenderers.Count > 128)
                {
                    _wokenRenderers.Clear();   // bound the cache; re-waking is cheap
                }

                _wokenRenderers.Add(primary);
            }
        }

        for (int i = 0; i < WakeRetries; i++)
        {
            Thread.Sleep(WakeRetryDelayMs);

            AutomationElement? focused;
            try { focused = AutomationElement.FocusedElement; }
            catch { focused = null; }

            if (focused is null || focused.Current.ProcessId == _ownProcessId)
            {
                continue;
            }

            if (IsEditable(focused))
            {
                Log($"woke renderer 0x{primary:X} after ~{(i + 1) * WakeRetryDelayMs}ms");
                return Describe(focused, "woken");
            }

            // Still non-editable after waking — log what the tree now reports so we can refine
            // IsEditable for this app even if it isn't accepted yet.
            Log($"  woke-attempt {i + 1}: {Snapshot(focused)}");
        }

        return null;
    }

    /// <summary>An editable text target: an enabled, keyboard-focusable, non-password Edit/Document
    /// that exposes a writable ValuePattern, or an Edit backed by a text-selecting TextPattern.</summary>
    private static bool IsEditable(AutomationElement element)
    {
        var info = element.Current;
        bool isEdit = info.ControlType == ControlType.Edit;
        bool isDoc = info.ControlType == ControlType.Document;
        if (!isEdit && !isDoc)
        {
            return false;
        }

        if (info.IsPassword || !info.IsEnabled || !info.IsKeyboardFocusable)
        {
            return false;
        }

        // A writable ValuePattern is the clearest signal (e.g. Telegram's Ui::InputField).
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value)
            && !((ValuePattern)value).Current.IsReadOnly)
        {
            return true;
        }

        // Otherwise a Chromium contenteditable (role="textbox", e.g. WhatsApp's message box) maps to
        // ControlType.Edit with a READ-ONLY ValuePattern but a real, text-selecting TextPattern. We
        // keep the Edit requirement so a static web page's Document (also TextPattern-capable for
        // screen readers) is never mistaken for a field.
        return isEdit
            && element.TryGetCurrentPattern(TextPattern.Pattern, out var text)
            && ((TextPattern)text).SupportedTextSelection != SupportedTextSelection.None;
    }

    private static Rectangle? ReadRect(AutomationElement element)
    {
        var r = element.Current.BoundingRectangle;   // System.Windows.Rect, physical screen pixels
        if (r.IsEmpty || double.IsInfinity(r.Width) || double.IsInfinity(r.Height) || r.Width <= 0 || r.Height <= 0)
        {
            return null;
        }

        return new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
    }

    private static nint SafeNativeWindowHandle(AutomationElement element)
    {
        try { return element.Current.NativeWindowHandle; }
        catch { return 0; }
    }

    // Send the MSAA WM_GETOBJECT(OBJID_CLIENT) handshake to a window; Chromium responds by building
    // its accessibility tree. We don't need the object itself — only the side effect — so release it.
    private static void WakeAccessibility(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        try
        {
            var iid = IID_IAccessible;
            if (AccessibleObjectFromWindow(hwnd, OBJID_CLIENT, ref iid, out object? acc) == 0 && acc is not null)
            {
                Marshal.ReleaseComObject(acc);
            }
        }
        catch { /* wake is best-effort; a failure just means no badge for this element */ }
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        nint hwnd, uint dwId, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object? ppvObject);

    // --- diagnostics (shared log with the focus watcher; on by default, AITR_FOCUS_LOG=0 disables) ---

    private FieldLocation Describe(AutomationElement element, string how)
    {
        var location = new FieldLocation(IsEditable(element), ReadRect(element));
        Log($"focusedElement ({how}): {Snapshot(element)} → editable={location.IsEditable} rect={location.Rect?.ToString() ?? "null"}");
        return location;
    }

    private static string Snapshot(AutomationElement e) =>
        $"ControlType={Safe(() => e.Current.ControlType.ProgrammaticName)} Class='{Safe(() => e.Current.ClassName)}' " +
        $"Name='{Safe(() => e.Current.Name)}' focusable={Safe(() => e.Current.IsKeyboardFocusable.ToString())} " +
        $"value={Safe(() => ValueState(e))} text={Safe(() => e.TryGetCurrentPattern(TextPattern.Pattern, out _).ToString())}";

    private static string ValueState(AutomationElement e) =>
        e.TryGetCurrentPattern(ValuePattern.Pattern, out var v)
            ? (((ValuePattern)v).Current.IsReadOnly ? "readonly" : "writable")
            : "none";

    private static readonly string? LogPath =
        string.Equals(Environment.GetEnvironmentVariable("AITR_FOCUS_LOG"), "0", StringComparison.Ordinal)
            ? null
            : (Environment.GetEnvironmentVariable("AITR_FOCUS_LOG") is { Length: > 0 } p && p != "1"
                ? p
                : Path.Combine(Path.GetTempPath(), "ai-translator-focus.log"));

    private static void Log(string message)
    {
        if (LogPath is null)
        {
            return;
        }

        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}   {message}{Environment.NewLine}"); }
        catch { /* diagnostics must never throw */ }
    }

    private static string Safe(Func<string> f)
    {
        try { return f() ?? string.Empty; }
        catch { return "<err>"; }
    }
}
