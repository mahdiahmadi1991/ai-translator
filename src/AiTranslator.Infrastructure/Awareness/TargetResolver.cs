using System.Collections.Generic;
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
/// and modern Chromium/WebView2/Electron.
/// </summary>
/// <remarks>
/// The WhatsApp case (diagnosed from %TEMP%\ai-translator-focus.log): WhatsApp is a WinUI 3 app
/// hosting WebView2. The OS-wide <see cref="AutomationElement.FocusedElement"/> stops at the WinUI
/// host panes (Microsoft.UI.Content.DesktopChildSiteBridge / InputSite) and never crosses the
/// ContentIsland boundary into the Chromium <c>contenteditable</c>. So when the focused element is not
/// itself editable we (1) find the real Chromium render-widget HWNDs parented under the foreground
/// window, (2) "wake" each one's accessibility tree with the MSAA <c>AccessibleObjectFromWindow</c>
/// handshake (what screen readers use), then (3) re-query the OS-wide focus and, failing that, drill
/// directly into each render widget's UIA subtree for the focused editable element.
///
/// A freshly woken renderer builds its tree asynchronously, so <see cref="Resolve"/> never blocks
/// waiting for it: while a render widget is within its post-wake grace window and nothing editable has
/// surfaced yet, it returns <see cref="FieldStatus.Pending"/> so the caller retries shortly instead of
/// tearing the badge down. The wake side effect is recorded per-HWND with a timestamp.
/// </remarks>
public sealed class TargetResolver : ITargetResolver
{
    private const uint OBJID_CLIENT = 0xFFFFFFFC;
    private const long WakeGraceMs = 1500;   // how long after a wake we keep reporting Pending

    private static readonly Guid IID_IAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly Dictionary<nint, long> _wokenAt = new();   // render HWND → TickCount64 of its wake
    private readonly object _wokenLock = new();

    public FieldResolution Resolve(nint windowHandle)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                Log("focusedElement: <null>");
                return FieldResolution.Unknown;
            }

            // Never treat our own floating box as a target (it has focus while the user types).
            if (focused.Current.ProcessId == _ownProcessId)
            {
                return FieldResolution.Unknown;
            }

            if (IsEditable(focused))
            {
                return Describe(focused, "direct");
            }

            // Non-editable focused element: maybe a WebView2/Chromium host whose tree isn't reachable.
            return TryResolveWebView(focused, windowHandle);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Any UIA/COM failure is "unresolved", never a crash — the caller runs on a thread whose
            // unhandled exception would kill the process.
            return FieldResolution.Unknown;
        }
    }

    /// <summary>
    /// Find the Chromium render-widget HWND(s) under the foreground window, wake their accessibility
    /// trees, then resolve the focused editable element via the OS-wide focus (fast path) or by
    /// drilling into each render widget's UIA subtree (fallback). Non-blocking: if a renderer was just
    /// woken and nothing editable has surfaced yet, reports <see cref="FieldStatus.Pending"/>.
    /// </summary>
    private FieldResolution TryResolveWebView(AutomationElement focusedRoot, nint foreground)
    {
        var childRenderers = FindChildChromium(foreground);

        // If the foreground window hosts its OWN Chromium renderer (a normal browser window, Electron,
        // or an in-process WebView2), the OS-wide focused element is authoritative for the active tab —
        // and we already saw it is non-editable, so the user simply is not in a field. We must NOT drill
        // here: DOM focus (HasKeyboardFocus) is "sticky" per tab/window, so drilling would surface a
        // background tab's or another browser window's field (the Chrome → "Gemini prompt" false badge).
        if (childRenderers.Exists(h => ClassOf(h) == "Chrome_RenderWidgetHostHWND"))
        {
            return Describe(focusedRoot, "non-editable");
        }

        // No in-window renderer → a WinUI 3 shell (WhatsApp) whose chat lives in a SEPARATE top-level
        // msedgewebview2 window of the same process family, which OS focus cannot reach. Only here do we
        // collect those related windows and drill for the keyboard-focused field.
        var renderHwnds = childRenderers;
        renderHwnds.AddRange(FindRelatedTopLevelChromium(foreground));
        nint focusedHwnd = SafeNativeWindowHandle(focusedRoot);
        if (focusedHwnd != 0)
        {
            renderHwnds.Add(focusedHwnd);
        }

        renderHwnds = DedupRenderFirst(renderHwnds);
        if (renderHwnds.Count == 0)
        {
            return Describe(focusedRoot, "non-editable");
        }

        bool firstWake = false;
        foreach (nint h in renderHwnds)
        {
            firstWake |= WakeOnce(h);
        }

        Log($"webview: firstWake={firstWake} renders=[{DescribeHwnds(renderHwnds)}]");

        // After waking, the OS-wide focus may now resolve into the contenteditable.
        var osFocused = SafeFocusedElement();
        if (osFocused is not null && osFocused.Current.ProcessId != _ownProcessId && IsEditable(osFocused))
        {
            return Describe(osFocused, "woken-os-focus");
        }

        // Drill each related render widget's subtree for the keyboard-focused editable element.
        foreach (nint h in renderHwnds)
        {
            var field = DrillFocusedEditable(h);
            if (field is not null)
            {
                return field;
            }
        }

        // Nothing editable yet. While a renderer is within its post-wake grace window the tree is
        // probably still building → ask the caller to retry; otherwise it is genuinely non-editable
        // (e.g. the WhatsApp message box lost DOM focus) → let the badge hide.
        return RecentlyWoken(renderHwnds)
            ? FieldResolution.Pending
            : Describe(focusedRoot, "non-editable");
    }

    /// <summary>Locate the focused editable element inside a single render widget's UIA subtree.
    /// FindFirst executes provider-side (one cross-process call, not one per node).</summary>
    private FieldResolution? DrillFocusedEditable(nint hwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            if (root is null)
            {
                return null;
            }

            var focused = root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.HasKeyboardFocusProperty, true));

            if (focused is null)
            {
                Log($"  drill 0x{hwnd:X}: no focused descendant");
                return null;
            }

            Log($"  drill 0x{hwnd:X} focused: {Snapshot(focused)}");
            return IsEditable(focused) ? Describe(focused, "drilled") : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log($"  drill 0x{hwnd:X}: {ex.GetType().Name}");
            return null;
        }
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
        // ControlType.Edit with a READ-ONLY ValuePattern but a real, text-selecting TextPattern. We keep
        // the Edit requirement so a static web page's Document (also TextPattern-capable for screen
        // readers) is never mistaken for a field.
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

    // --- Chromium window discovery + accessibility wake ----------------------------------------

    /// <summary>Chromium windows parented INSIDE the foreground window (in-process WebView2 / Electron /
    /// a normal browser window). EnumChildWindows recurses and crosses process boundaries.</summary>
    private static List<nint> FindChildChromium(nint top)
    {
        var found = new List<nint>();
        if (top == 0)
        {
            return found;
        }

        try
        {
            EnumChildWindows(top, (h, _) =>
            {
                if (ClassOf(h).StartsWith("Chrome_", StringComparison.Ordinal))
                {
                    found.Add(h);
                }

                return true;
            }, 0);
        }
        catch { /* best-effort */ }

        return found;
    }

    /// <summary>
    /// SEPARATE top-level Chromium windows belonging to the foreground app's process family and laid
    /// over its window — the "WhatsApp Business" case (a standalone msedgewebview2 window, not a child,
    /// not owned, whose process descends from WhatsApp.Root.exe). The geometric overlap test keeps us
    /// from grabbing a DIFFERENT background window of a multi-window browser.
    /// </summary>
    private static List<nint> FindRelatedTopLevelChromium(nint top)
    {
        var found = new List<nint>();
        uint fgPid = PidOf(top);
        if (fgPid == 0)
        {
            return found;
        }

        var family = ProcessFamily(fgPid);
        RECT fgRect = RectOf(top);

        try
        {
            EnumWindows((w, _) =>
            {
                if (w != top
                    && IsWindowVisible(w)
                    && ClassOf(w).StartsWith("Chrome_", StringComparison.Ordinal)
                    && family.Contains(PidOf(w))
                    && OverlapsMostly(RectOf(w), fgRect))
                {
                    found.Add(w);
                    EnumChildWindows(w, (h, _) =>
                    {
                        if (ClassOf(h).StartsWith("Chrome_", StringComparison.Ordinal))
                        {
                            found.Add(h);
                        }

                        return true;
                    }, 0);
                }

                return true;
            }, 0);
        }
        catch { /* best-effort */ }

        return found;
    }

    private static List<nint> DedupRenderFirst(List<nint> hwnds)
    {
        var unique = new List<nint>();
        var seen = new HashSet<nint>();
        foreach (nint h in hwnds)
        {
            if (h != 0 && seen.Add(h))
            {
                unique.Add(h);
            }
        }

        // Renderers first — the actual drill target; widget hosts / placeholders are lower priority.
        unique.Sort((a, b) => RenderRank(ClassOf(a)).CompareTo(RenderRank(ClassOf(b))));
        return unique;
    }

    private static int RenderRank(string cls) =>
        cls == "Chrome_RenderWidgetHostHWND" ? 0 : 1;

    /// <summary>True when at least half of window <paramref name="w"/> lies inside <paramref name="fg"/>
    /// (the webview is visually hosted over the foreground app).</summary>
    private static bool OverlapsMostly(RECT w, RECT fg)
    {
        long ix = Math.Max(0, Math.Min(w.right, fg.right) - Math.Max(w.left, fg.left));
        long iy = Math.Max(0, Math.Min(w.bottom, fg.bottom) - Math.Max(w.top, fg.top));
        long intersection = ix * iy;
        long area = (long)Math.Max(1, w.right - w.left) * Math.Max(1, w.bottom - w.top);
        return intersection * 2 >= area;
    }

    private static RECT RectOf(nint hwnd)
    {
        return GetWindowRect(hwnd, out RECT r) ? r : default;
    }

    private static uint PidOf(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }

    /// <summary>The given process id plus every process descended from it (WebView2 spawns
    /// msedgewebview2.exe as a child of the host app, so its window belongs to this family).</summary>
    private static HashSet<uint> ProcessFamily(uint rootPid)
    {
        var family = new HashSet<uint> { rootPid };
        var children = new Dictionary<uint, List<uint>>();

        try
        {
            nint snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snap == InvalidHandle)
            {
                return family;
            }

            try
            {
                var pe = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
                if (Process32First(snap, ref pe))
                {
                    do
                    {
                        if (!children.TryGetValue(pe.th32ParentProcessID, out var kids))
                        {
                            children[pe.th32ParentProcessID] = kids = new List<uint>();
                        }

                        kids.Add(pe.th32ProcessID);
                    }
                    while (Process32Next(snap, ref pe));
                }
            }
            finally
            {
                CloseHandle(snap);
            }

            var queue = new Queue<uint>();
            queue.Enqueue(rootPid);
            while (queue.Count > 0)
            {
                uint p = queue.Dequeue();
                if (children.TryGetValue(p, out var kids))
                {
                    foreach (uint k in kids)
                    {
                        if (family.Add(k))
                        {
                            queue.Enqueue(k);
                        }
                    }
                }
            }
        }
        catch { /* snapshot is best-effort; fall back to just the root pid */ }

        return family;
    }

    /// <summary>Wake a window's accessibility tree the first time we see it, recording when (the
    /// timestamp drives the post-wake grace window). Returns true if this was its first wake.</summary>
    private bool WakeOnce(nint hwnd)
    {
        if (hwnd == 0)
        {
            return false;
        }

        bool first;
        lock (_wokenLock)
        {
            first = !_wokenAt.ContainsKey(hwnd);
            if (first)
            {
                if (_wokenAt.Count > 256)
                {
                    PruneWoken();   // bound the cache; re-waking is cheap
                }

                // Record only the FIRST wake: the grace window must measure time since the tree began
                // building, not be refreshed on every resolve (which would keep the badge alive forever).
                _wokenAt[hwnd] = Environment.TickCount64;
            }
        }

        if (first)
        {
            WakeAccessibility(hwnd);
        }

        return first;
    }

    /// <summary>True while any of the given render windows is within its post-wake grace window.</summary>
    private bool RecentlyWoken(List<nint> hwnds)
    {
        long now = Environment.TickCount64;
        lock (_wokenLock)
        {
            foreach (nint h in hwnds)
            {
                if (_wokenAt.TryGetValue(h, out long t) && now - t < WakeGraceMs)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void PruneWoken()
    {
        long now = Environment.TickCount64;
        var stale = new List<nint>();
        foreach (var kv in _wokenAt)
        {
            if (now - kv.Value >= WakeGraceMs)
            {
                stale.Add(kv.Key);
            }
        }

        foreach (nint h in stale)
        {
            _wokenAt.Remove(h);
        }

        if (_wokenAt.Count > 256)
        {
            _wokenAt.Clear();   // pathological fallback
        }
    }

    // Send the MSAA WM_GETOBJECT(OBJID_CLIENT) handshake; Chromium responds by building its a11y tree.
    // We don't need the object itself — only the side effect — so release it.
    private static void WakeAccessibility(nint hwnd)
    {
        try
        {
            var iid = IID_IAccessible;
            if (AccessibleObjectFromWindow(hwnd, OBJID_CLIENT, ref iid, out object? acc) == 0 && acc is not null)
            {
                Marshal.ReleaseComObject(acc);
            }
        }
        catch { /* wake is best-effort */ }
    }

    private static nint SafeNativeWindowHandle(AutomationElement element)
    {
        try { return element.Current.NativeWindowHandle; }
        catch { return 0; }
    }

    private static AutomationElement? SafeFocusedElement()
    {
        try { return AutomationElement.FocusedElement; }
        catch { return null; }
    }

    private static string ClassOf(nint hwnd)
    {
        var buf = new char[256];
        int n = GetClassName(hwnd, buf, buf.Length);
        return n > 0 ? new string(buf, 0, n) : string.Empty;
    }

    // --- P/Invoke (raw DllImport; CsWin32 is used elsewhere but these are local to the resolver) ---

    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private static readonly nint InvalidHandle = new(-1);

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint hWndParent, EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(nint hWnd, [Out] char[] lpClassName, int nMaxCount);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        nint hwnd, uint dwId, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object? ppvObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(nint hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(nint hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    // --- diagnostics (shared log with the focus watcher; on by default, AITR_FOCUS_LOG=0 disables) ---

    private FieldResolution Describe(AutomationElement element, string how)
    {
        bool editable = IsEditable(element);
        Rectangle? rect = editable ? ReadRect(element) : null;
        Log($"focusedElement ({how}): {Snapshot(element)} → editable={editable} rect={rect?.ToString() ?? "null"}");
        return editable ? FieldResolution.Editable(rect) : FieldResolution.NotEditable;
    }

    private static string DescribeHwnds(List<nint> hwnds)
    {
        var parts = new List<string>(hwnds.Count);
        foreach (nint h in hwnds)
        {
            parts.Add($"0x{h:X}:{ClassOf(h)}");
        }

        return string.Join(", ", parts);
    }

    private static string Snapshot(AutomationElement e) =>
        $"ControlType={Safe(() => e.Current.ControlType.ProgrammaticName)} Class='{Safe(() => e.Current.ClassName)}' " +
        $"Name='{Safe(() => e.Current.Name)}' focusable={Safe(() => e.Current.IsKeyboardFocusable.ToString())} " +
        $"hasFocus={Safe(() => e.Current.HasKeyboardFocus.ToString())} " +
        $"value={Safe(() => ValueState(e))} text={Safe(() => e.TryGetCurrentPattern(TextPattern.Pattern, out _).ToString())}";

    private static string ValueState(AutomationElement e) =>
        e.TryGetCurrentPattern(ValuePattern.Pattern, out var v)
            ? (((ValuePattern)v).Current.IsReadOnly ? "readonly" : "writable")
            : "none";

    private static readonly string? LogPath = ResolveLogPath();

    private static string? ResolveLogPath()
    {
        // Opt-in diagnostics: AITR_FOCUS_LOG=1 → %TEMP%\ai-translator-focus.log, or set a full path. Off otherwise.
        var v = Environment.GetEnvironmentVariable("AITR_FOCUS_LOG");
        if (string.IsNullOrWhiteSpace(v) || v == "0")
        {
            return null;
        }

        return v == "1" ? Path.Combine(Path.GetTempPath(), "ai-translator-focus.log") : v;
    }

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
