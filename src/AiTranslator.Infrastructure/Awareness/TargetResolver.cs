using System.Drawing;
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
/// rectangle (physical pixels). Caret-precise placement (TextPattern2 / GetGUIThreadInfo) and the
/// explicit IAccessible2 "wake the Chromium renderer" dance from the plan are deferred until manual
/// per-app testing (Task 5) shows they are actually needed — modern UIA covers the common cases.
/// </remarks>
public sealed class TargetResolver : ITargetResolver
{
    private readonly object _gate = new();
    private readonly int _ownProcessId = Environment.ProcessId;
    private nint _cachedHandle;
    private AutomationElement? _cachedElement;   // last editable field resolved for _cachedHandle

    public FieldLocation? Resolve(nint windowHandle)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                return null;
            }

            // Never treat our own floating box as a target (it has focus while the user types) — that
            // would mis-anchor the badge and, worse, pollute the cache so TrySetText writes into us.
            if (focused.Current.ProcessId == _ownProcessId)
            {
                return null;
            }

            bool editable = IsEditable(focused);
            if (editable)
            {
                // Remember the element so TrySetText can write into it later WITHOUT moving focus
                // away from the floating box.
                lock (_gate)
                {
                    _cachedHandle = windowHandle;
                    _cachedElement = focused;
                }
            }

            return new FieldLocation(editable, ReadRect(focused));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Any UIA/COM failure (element vanished, provider mid-teardown, ArgumentException/
            // Win32Exception from a flaky Chromium/Electron provider, …) is "unresolved", never a
            // crash — this runs on a thread whose unhandled exception would kill the process.
            return null;
        }
    }

    public bool TrySetText(nint windowHandle, string text)
    {
        AutomationElement? element;
        lock (_gate)
        {
            element = _cachedHandle == windowHandle ? _cachedElement : null;
        }

        if (element is null)
        {
            return false;
        }

        try
        {
            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            {
                return false;
            }

            var value = (ValuePattern)pattern;
            if (value.Current.IsReadOnly)
            {
                return false;
            }

            value.SetValue(text);   // direct, focus-preserving

            // A Chromium contenteditable (WhatsApp, Monaco, …) accepts SetValue WITHOUT error but
            // ignores it. Verify the value actually took; if not, report failure so the caller falls
            // back to clipboard paste (which a real Ctrl+V does apply to those fields).
            return ReadValue(value) == text;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;   // provider rejected SetValue → fall back to clipboard paste
        }
    }

    private static string ReadValue(ValuePattern value)
    {
        try { return value.Current.Value ?? string.Empty; }
        catch { return string.Empty; }
    }

    /// <summary>An editable text target: an enabled, keyboard-focusable, non-password Edit/Document
    /// that exposes a writable ValuePattern, or an Edit backed by a TextPattern.</summary>
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

        // Otherwise an Edit backed by a TextPattern is still typeable — the WhatsApp/Chromium case,
        // where the contenteditable exposes a READ-ONLY ValuePattern but a real TextPattern. A Document
        // without a writable value is treated as read-only content (a web page), not a field.
        return isEdit && element.TryGetCurrentPattern(TextPattern.Pattern, out _);
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
}
