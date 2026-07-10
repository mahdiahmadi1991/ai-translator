using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;                 // WPF Clipboard (STA) — Infrastructure sets UseWPF=true.
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Threading;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Awareness;
using AiTranslator.Core.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

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
    private const int WM_LBUTTONUP = 0x0202;
    private const int DebounceMs = 180;         // let the selection settle after the mouse is released
    private const int ResolveTimeoutMs = 400;   // never block indefinitely on cross-process UIA
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
            if (nCode >= 0 && (int)wParam == WM_LBUTTONUP)
            {
                _debounce!.Stop();
                _debounce.Start();   // coalesce; read the selection once the drag has settled
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
        _ = Task.Run(() =>
        {
            SelectedText? selection = ReadWithTimeout(allowClipboard: false);
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

    /// <summary>Hotkey path: read on the calling (UI) thread, with a clipboard-copy fallback.</summary>
    public SelectedText? CaptureCurrentSelection() => ReadSelection(allowClipboard: true);

    private SelectedText? ReadWithTimeout(bool allowClipboard)
    {
        try
        {
            var task = Task.Run(() => ReadSelection(allowClipboard));
            return task.Wait(ResolveTimeoutMs) ? task.Result : null;
        }
        catch
        {
            return null;
        }
    }

    private SelectedText? ReadSelection(bool allowClipboard)
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

            var focused = AutomationElement.FocusedElement;
            if (focused is not null && focused.Current.ProcessId != _ownProcessId
                && focused.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
            {
                var ranges = ((TextPattern)pattern).GetSelection();
                if (ranges is { Length: > 0 })
                {
                    string text = SafeGetText(ranges[0]);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        Rectangle? bounds = UnionRects(SafeGetRects(ranges[0]));
                        return new SelectedText(text.Trim(), bounds, (nint)fg, exeName, IsEditable(focused));
                    }
                }
            }

            // No UIA selection. On the explicit hotkey path only, fall back to a clipboard copy.
            if (allowClipboard)
            {
                string? copied = CopySelectionViaClipboard();
                if (!string.IsNullOrWhiteSpace(copied))
                {
                    return new SelectedText(copied.Trim(), null, (nint)fg, exeName, false);
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

    // Send Ctrl+C, read what the app placed on the clipboard, then restore the previous clipboard text.
    private static string? CopySelectionViaClipboard()
    {
        string? previous = SafeGetClipboardText();
        try
        {
            try { Clipboard.Clear(); } catch { }
            SendCtrlC();
            Thread.Sleep(90);   // give the target app a moment to copy (explicit hotkey path only)
            return SafeGetClipboardText();
        }
        finally
        {
            if (previous is not null)
            {
                try { Clipboard.SetText(previous); } catch { }
            }
        }
    }

    private static string? SafeGetClipboardText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch { return null; }
    }

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
}
