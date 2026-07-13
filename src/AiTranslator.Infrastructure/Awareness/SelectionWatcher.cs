using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;                 // WPF Clipboard (STA) — Infrastructure sets UseWPF=true.
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Threading;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Awareness;
using AiTranslator.Core.Models;
using AiTranslator.Infrastructure.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Point = System.Drawing.Point;   // disambiguate from System.Windows.Point (this file uses both namespaces)

namespace AiTranslator.Infrastructure.Awareness;

/// <summary>
/// Detects text selections anywhere on screen (the "read" mode). A low-level mouse hook on a dedicated
/// thread with a message pump watches for left-button-up; a short debounce later the current selection
/// is read via UI Automation's <see cref="TextPattern"/> (text + on-screen bounds) off the pump thread.
/// The selection hotkey reads on demand via <see cref="CaptureCurrentSelection"/>, with a clipboard-copy
/// fallback for apps that do not expose a UIA text selection. Mirrors <see cref="FocusWatcher"/>'s
/// threading model (ADR-0003): the hook thread never blocks on cross-process UIA.
/// </summary>
public sealed class SelectionWatcher : ISelectionWatcher
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int DebounceMs = 180;         // let the selection settle after the mouse is released
    private const int ResolveTimeoutMs = 1000;  // budget for UIA read + (best-effort) clipboard fallback
    private const int DragThresholdPx = 4;      // a press-move-release this far counts as a text selection
    private const int MaxChars = 5000;          // cap what we read/translate

    private readonly Func<AppSettings> _settingsProvider;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private readonly ManualResetEventSlim _ready = new(initialState: false);

    private Thread? _thread;
    private Dispatcher? _dispatcher;
    private DispatcherTimer? _debounce;
    private LowLevelMouseProc? _proc;   // rooted so the GC never collects the hook delegate
    private nint _hook;
    private int _generation;
    private bool _hadSelection;

    // Gesture tracking (written and read on the hook/pump thread only — no locking needed).
    private Point _downPoint;
    private Point _upPoint;
    private bool _gestureWasSelection;   // last left-button-up completed a drag (a deliberate selection)

    public SelectionWatcher(Func<AppSettings> settingsProvider) => _settingsProvider = settingsProvider;

    public event EventHandler<SelectedText>? SelectionChanged;
    public event EventHandler? SelectionCleared;

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _ready.Reset();
        _thread = new Thread(RunMessageLoop) { IsBackground = true, Name = "AiTranslator.SelectionWatcher" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        _ready.Wait(2000);
        _dispatcher?.InvokeShutdown();
        _thread.Join(2000);
        _thread = null;
        _dispatcher = null;
    }

    public void Dispose()
    {
        Stop();
        _ready.Dispose();
    }

    private void RunMessageLoop()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _proc = OnMouse;
        _debounce = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(DebounceMs),
        };
        _debounce.Tick += OnDebounceTick;

        _hook = SetWindowsHookExW(WH_MOUSE_LL, _proc, GetModuleHandleW(null), 0);
        Log(_hook != 0 ? $"hook installed: 0x{_hook:X}" : $"hook install FAILED (err={Marshal.GetLastWin32Error()})");
        _ready.Set();

        Dispatcher.Run();

        _debounce.Stop();
        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    private nint OnMouse(int nCode, nint wParam, nint lParam)
    {
        // Invoked on the pump thread for every mouse event system-wide — must be fast and never throw.
        try
        {
            if (nCode >= 0)
            {
                switch ((int)wParam)
                {
                    case WM_LBUTTONDOWN:
                        _downPoint = ReadPoint(lParam);
                        break;

                    case WM_LBUTTONUP:
                        _upPoint = ReadPoint(lParam);
                        _gestureWasSelection =
                            Math.Abs(_upPoint.X - _downPoint.X) >= DragThresholdPx ||
                            Math.Abs(_upPoint.Y - _downPoint.Y) >= DragThresholdPx;
                        Log($"mouse-up drag={_gestureWasSelection} at {_upPoint.X},{_upPoint.Y}");
                        _debounce!.Stop();
                        _debounce.Start();   // coalesce; read the selection once the drag has settled
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // never let a bad event crash the hook (or the app)
        }

        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounce!.Stop();
        int generation = ++_generation;

        // Snapshot the gesture on the pump thread. A drag is a deliberate selection, so the auto path
        // may fall back to a clipboard copy (accessibility exposes no selection in Chromium apps like
        // Teams/WhatsApp); a plain click never copies. The drop point anchors the icon.
        bool allowClipboard = _gestureWasSelection;
        Point anchor = _upPoint;

        _ = Task.Run(() =>
        {
            SelectedText? selection = ReadWithTimeout(allowClipboard, anchor);
            _dispatcher?.InvokeAsync(() =>
            {
                if (generation != _generation)
                {
                    return;   // superseded by a newer mouse-up
                }

                if (selection is not null)
                {
                    _hadSelection = true;
                    SelectionChanged?.Invoke(this, selection);
                }
                else if (_hadSelection)
                {
                    _hadSelection = false;
                    SelectionCleared?.Invoke(this, EventArgs.Empty);
                }
            });
        });
    }

    /// <summary>Hotkey path: read on the calling (UI) thread, with a clipboard-copy fallback. The icon
    /// (if shown) anchors at the cursor since there is no drag drop-point here.</summary>
    public SelectedText? CaptureCurrentSelection() => ReadSelection(allowClipboard: true, CursorPoint());

    private SelectedText? ReadWithTimeout(bool allowClipboard, Point anchor)
    {
        try
        {
            var task = Task.Run(() => ReadSelection(allowClipboard, anchor));
            return task.Wait(ResolveTimeoutMs) ? task.Result : null;
        }
        catch
        {
            return null;
        }
    }

    private SelectedText? ReadSelection(bool allowClipboard, Point anchor)
    {
        try
        {
            HWND fg = PInvoke.GetForegroundWindow();
            if (fg.IsNull)
            {
                return null;
            }

            uint threadId = PInvoke.GetWindowThreadProcessId(fg, out uint pid);
            if (pid == 0 || pid == _ownProcessId)
            {
                return null;
            }

            string? exePath = ResolveExe(pid);
            var settings = _settingsProvider();
            if (exePath is null || !AppActivationPolicy.ShouldActivate(exePath, settings.Blocklist))
            {
                return null;   // blocked app (or unknown)
            }

            string exeName = System.IO.Path.GetFileName(exePath);
            Log($"read: exe='{exeName}' allowClipboard={allowClipboard}");

            var focused = AutomationElement.FocusedElement;
            if (focused is null || focused.Current.ProcessId == _ownProcessId)
            {
                Log("  focused: <null or own process>");
            }
            else
            {
                bool hasText = focused.TryGetCurrentPattern(TextPattern.Pattern, out var pattern);
                Log($"  focused: {Snapshot(focused)} textPattern={hasText}");
                if (hasText)
                {
                    var ranges = ((TextPattern)pattern).GetSelection();
                    Log($"  selection: ranges={ranges?.Length ?? 0}");
                    if (ranges is { Length: > 0 })
                    {
                        string text = SafeGetText(ranges[0]);
                        Rectangle? bounds = UnionRects(SafeGetRects(ranges[0]));
                        Log($"  selection text len={text.Length} bounds={bounds?.ToString() ?? "null"}");
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            // Anchor on the drop point / cursor if UIA gives us no bounding rectangle,
                            // so the icon always appears where the user finished selecting.
                            Rectangle resolved = bounds ?? new Rectangle(anchor.X, anchor.Y, 1, 1);
                            return new SelectedText(text.Trim(), resolved, (nint)fg, exeName, IsEditable(focused));
                        }
                    }
                }
            }

            // No UIA selection (typical for Chromium apps). If this came from a real drag selection
            // (auto path) or the explicit hotkey, fall back to a Ctrl+C copy. Anchor the icon at the
            // drop point / cursor since the clipboard gives us no on-screen bounds.
            if (allowClipboard)
            {
                string? copied = CopySelectionViaClipboard();
                Log($"  clipboard fallback: len={copied?.Length ?? 0}");
                if (!string.IsNullOrWhiteSpace(copied))
                {
                    string text = copied.Trim();
                    if (text.Length > MaxChars)
                    {
                        text = text[..MaxChars];   // cap huge selections (whole-document Ctrl+C) like the UIA path
                    }

                    Rectangle anchorBounds = new(anchor.X, anchor.Y, 1, 1);
                    return new SelectedText(text, anchorBounds, (nint)fg, exeName, false);
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static string SafeGetText(TextPatternRange range)
    {
        try { return range.GetText(MaxChars) ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static System.Windows.Rect[] SafeGetRects(TextPatternRange range)
    {
        try { return range.GetBoundingRectangles() ?? []; }
        catch { return []; }
    }

    private static Rectangle? UnionRects(System.Windows.Rect[] rects)
    {
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        bool any = false;
        foreach (var r in rects)
        {
            if (r.IsEmpty || r.Width <= 0 || r.Height <= 0 || double.IsInfinity(r.Width) || double.IsInfinity(r.Height))
            {
                continue;
            }

            any = true;
            left = Math.Min(left, r.Left);
            top = Math.Min(top, r.Top);
            right = Math.Max(right, r.Right);
            bottom = Math.Max(bottom, r.Bottom);
        }

        if (!any || right <= left || bottom <= top)
        {
            return null;
        }

        return Rectangle.FromLTRB((int)left, (int)top, (int)right, (int)bottom);
    }

    private static bool IsEditable(AutomationElement element)
    {
        try
        {
            var info = element.Current;
            if (info.IsPassword || !info.IsEnabled)
            {
                return false;
            }

            return element.TryGetCurrentPattern(ValuePattern.Pattern, out var v)
                && !((ValuePattern)v).Current.IsReadOnly;
        }
        catch
        {
            return false;
        }
    }

    // Send Ctrl+C, read what the app copies, then restore the previous clipboard. Runs on a dedicated
    // STA thread — the WPF Clipboard requires STA, and the auto path reads on an MTA thread-pool thread;
    // this also keeps the mouse-hook pump thread from ever blocking on the clipboard.
    private static string? CopySelectionViaClipboard() => RunSta(() =>
    {
        string? previous;
        try
        {
            // Don't risk clobbering a non-text clipboard (image / copied files) — bail if that's what's there.
            if (!Clipboard.ContainsText() && (Clipboard.ContainsImage() || Clipboard.ContainsFileDropList()))
            {
                return null;
            }

            previous = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch { previous = null; }

        try
        {
            SendCtrlC();
            for (int attempt = 0; attempt < 12; attempt++)   // poll up to ~360ms for the app to copy
            {
                Thread.Sleep(30);
                string? current = SafeGetClipboardText();
                if (!string.IsNullOrEmpty(current) && current != previous)
                {
                    return current;
                }
            }

            return null;
        }
        finally
        {
            if (previous is not null)
            {
                try { Clipboard.SetText(previous); } catch { /* best effort */ }
            }
        }
    });

    // Run a clipboard operation on a short-lived STA thread and wait (bounded) for its result.
    private static string? RunSta(Func<string?> func)
    {
        string? result = null;
        var thread = new Thread(() => { try { result = func(); } catch { /* never throw across threads */ } })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(1500);
        return result;
    }

    private static Point ReadPoint(nint lParam)
    {
        // lParam points at MSLLHOOKSTRUCT; its first member is POINT { int x; int y; } in physical pixels.
        try { return new Point(Marshal.ReadInt32(lParam, 0), Marshal.ReadInt32(lParam, 4)); }
        catch { return Point.Empty; }
    }

    private static Point CursorPoint()
    {
        // System.Drawing.Point is laid out as { int X; int Y; }, matching Win32 POINT.
        try { return GetCursorPos(out Point p) ? p : Point.Empty; }
        catch { return Point.Empty; }
    }

    private static string? SafeGetClipboardText() => StaClipboard.GetText();   // already on our STA thread

    private static void SendCtrlC()
    {
        const byte VK_CONTROL = 0x11, VK_C = 0x43;
        const uint KEYUP = 0x0002;
        keybd_event(VK_CONTROL, 0, 0, 0);
        keybd_event(VK_C, 0, 0, 0);
        keybd_event(VK_C, 0, KEYUP, 0);
        keybd_event(VK_CONTROL, 0, KEYUP, 0);
    }

    private static string? ResolveExe(uint pid)
    {
        try
        {
            using var handle = PInvoke.OpenProcess_SafeHandle(
                PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, bInheritHandle: false, pid);
            if (handle.IsInvalid)
            {
                return null;
            }

            Span<char> buffer = new char[1024];
            uint size = (uint)buffer.Length;
            return PInvoke.QueryFullProcessImageName(handle, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, ref size)
                ? new string(buffer[..(int)size])
                : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    // --- diagnostics (OPT-IN: off unless AITR_SELECTION_LOG is set, so we never write window titles /
    // selection metadata to %TEMP% in normal use. Set it to "1" for the default temp path, or a full path.) ---

    private static readonly string? LogPath = ResolveLogPath();

    private static string? ResolveLogPath()
    {
        var v = Environment.GetEnvironmentVariable("AITR_SELECTION_LOG");
        if (string.IsNullOrWhiteSpace(v) || string.Equals(v, "0", StringComparison.Ordinal))
        {
            return null;   // disabled by default
        }

        return v == "1"
            ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ai-translator-selection.log")
            : v;   // a caller-supplied path
    }

    private static void Log(string message)
    {
        if (LogPath is null)
        {
            return;
        }

        try { System.IO.File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}"); }
        catch { /* diagnostics must never throw */ }
    }

    private static string Snapshot(AutomationElement e)
    {
        string S(Func<string> f) { try { return f() ?? ""; } catch { return "<err>"; } }
        return $"ControlType={S(() => e.Current.ControlType.ProgrammaticName)} Class='{S(() => e.Current.ClassName)}' " +
               $"Name='{S(() => e.Current.Name)}' pid={S(() => e.Current.ProcessId.ToString())}";
    }

    // --- P/Invoke (raw DllImport for the mouse hook; CsWin32 covers the process/window calls) ---

    private delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SetWindowsHookExW(int idHook, LowLevelMouseProc lpfn, nint hmod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nint dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point lpPoint);
}
