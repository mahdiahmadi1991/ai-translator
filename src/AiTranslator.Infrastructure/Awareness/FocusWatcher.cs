using System.IO;
using System.Windows.Threading;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Awareness;
using AiTranslator.Core.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Accessibility;

namespace AiTranslator.Infrastructure.Awareness;

/// <summary>
/// Watches system-wide focus/foreground changes via <c>SetWinEventHook</c> (M2, ADR-0003) and raises
/// <see cref="FieldFocused"/> when an editable field gains focus inside an allowlisted app, or
/// <see cref="FieldUnfocused"/> when focus leaves it. The hook runs on a dedicated STA thread with a
/// WPF <see cref="Dispatcher"/> message pump (out-of-context WinEvents are delivered on the hooking
/// thread, which must pump messages).
/// </summary>
/// <remarks>
/// Per ADR-0003, cross-process UIA resolution must never block the hook/pump thread, so the cheap
/// allowlist decision runs on the pump thread but <see cref="ITargetResolver"/> is invoked on a
/// worker with a hard timeout; the result is marshalled back onto the pump thread (so all state stays
/// single-threaded) before <see cref="FieldFocused"/> is raised. A monotonically increasing
/// generation counter drops results from superseded focus changes. Events are raised on the pump
/// thread — consumers marshal to their UI thread themselves (non-blocking).
/// </remarks>
public sealed class FocusWatcher : IFocusWatcher
{
    private const int OBJID_CURSOR = -9;                 // skip mouse-cursor object noise on FOCUS
    private const int LocationDebounceMs = 80;           // coalesce LOCATIONCHANGE bursts
    private const int ResolveTimeoutMs = 600;            // ADR-0003: bounded UIA — room for a one-shot WebView2 a11y wake

    // Optional diagnostic trace: set AITR_FOCUS_LOG=1 (→ %TEMP%\ai-translator-focus.log) or to a path.
    private static readonly string? DebugLogPath = ResolveDebugLogPath();

    private readonly Func<AppSettings> _settingsProvider;
    private readonly ITargetResolver? _targetResolver;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private readonly ManualResetEventSlim _ready = new(initialState: false);

    private Thread? _thread;
    private Dispatcher? _dispatcher;
    private DispatcherTimer? _locationDebounce;
    private WINEVENTPROC? _proc;                          // rooted so the GC never moves/collects it
    private UnhookWinEventSafeHandle? _foregroundHook;
    private UnhookWinEventSafeHandle? _focusHook;
    private UnhookWinEventSafeHandle? _locationHook;      // scoped to the active field's thread

    private HWND _activeHwnd;
    private uint _activeThreadId;
    private string _activeExe = string.Empty;
    private bool _fieldActive;
    private int _focusGeneration;                         // drops resolves from superseded focus changes

    /// <param name="settingsProvider">Supplies the current settings (allowlist/blocklist) per event.</param>
    /// <param name="targetResolver">
    /// Optional field classifier/locator. When absent, every focus in an allowlisted app is treated as
    /// a field (no editability check, null rect) — the M1 behaviour. Task 5 wires the real resolver.
    /// </param>
    public FocusWatcher(Func<AppSettings> settingsProvider, ITargetResolver? targetResolver = null)
    {
        _settingsProvider = settingsProvider;
        _targetResolver = targetResolver;
    }

    public event EventHandler<FocusedField>? FieldFocused;
    public event EventHandler? FieldUnfocused;

    public void Start()
    {
        if (_thread is not null)
        {
            return;   // idempotent
        }

        _ready.Reset();
        _thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "AiTranslator.FocusWatcher",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        // Wait until the hook thread has published its dispatcher (fixes the start/stop race where a
        // Stop racing a just-started thread would leak the thread and its global hooks forever).
        _ready.Wait(millisecondsTimeout: 2000);
        _dispatcher?.InvokeShutdown();   // ends Dispatcher.Run(); the hook thread then disposes its hooks
        _thread.Join(millisecondsTimeout: 2000);
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
        _proc = OnWinEvent;
        _locationDebounce = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(LocationDebounceMs),
        };
        _locationDebounce.Tick += OnLocationDebounceTick;
        _ready.Set();   // publish the dispatcher before Stop() can act on it

        _foregroundHook = Hook(PInvoke.EVENT_SYSTEM_FOREGROUND);
        _focusHook = Hook(PInvoke.EVENT_OBJECT_FOCUS);

        Dispatcher.Run();

        // Dispatcher has shut down — release the hooks on this (the installing) thread.
        _locationDebounce.Stop();
        _locationHook?.Dispose();
        _focusHook?.Dispose();
        _foregroundHook?.Dispose();
    }

    private UnhookWinEventSafeHandle Hook(uint @event, uint idThread = 0)
        => PInvoke.SetWinEventHook(
            @event, @event, hmodWinEventProc: null, _proc!,
            idProcess: 0, idThread,
            PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS);

    private void OnWinEvent(
        HWINEVENTHOOK hook, uint @event, HWND hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        // This is invoked by the message pump; an exception unwinding to the top of the background
        // thread would terminate the process, so nothing may escape here.
        try
        {
            if (hwnd.IsNull)
            {
                return;
            }

            if (@event == PInvoke.EVENT_OBJECT_LOCATIONCHANGE)
            {
                // Something on the active window's thread moved/resized — re-anchor after a debounce.
                // The location hook is already scoped to the active thread, so any event it delivers
                // is relevant.
                if (_fieldActive && _targetResolver is not null)
                {
                    _locationDebounce!.Stop();
                    _locationDebounce.Start();
                }

                return;
            }

            if (idObject == OBJID_CURSOR)
            {
                return;   // FOREGROUND/FOCUS for the mouse cursor — not a real focus change
            }

            HandleFocusChange();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Swallow — a bad event must never crash the watcher (or the app).
        }
    }

    private void OnLocationDebounceTick(object? sender, EventArgs e)
    {
        _locationDebounce!.Stop();
        if (_fieldActive && !_activeHwnd.IsNull)
        {
            // Pure re-anchor: refresh the rect only; never re-decide activation (a transient
            // non-editable resolve while dragging must not make the badge vanish mid-typing).
            ResolveAndRaiseAsync(_activeHwnd, _activeExe, ++_focusGeneration, isReanchor: true);
        }
    }

    private void HandleFocusChange()
    {
        // Identify the app by the FOREGROUND top-level window, not the focus-event hwnd. For WebView2
        // apps (e.g. WhatsApp) the focus event's window belongs to msedgewebview2.exe, but the visible
        // app — and the right injection target — is the foreground window (WhatsApp.Root.exe). The
        // field itself is resolved separately via UIA's system-wide focused element.
        HWND fg = PInvoke.GetForegroundWindow();
        if (fg.IsNull)
        {
            return;
        }

        uint threadId = PInvoke.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == 0 || pid == _ownProcessId)
        {
            return;   // our own windows / unknown — leave current state untouched
        }

        string? exePath = ResolveExe(pid);   // cheap kernel call — fine on the pump thread
        var settings = _settingsProvider();
        bool allow = exePath is not null
            && AppActivationPolicy.ShouldActivate(exePath, settings.Blocklist);
        DebugLog($"focus: fg=0x{(nint)fg:X} pid={pid} exe='{exePath}' allow={allow}");

        if (!allow)
        {
            Deactivate();
            return;
        }

        // Allowlisted: mark active immediately (cheap) and resolve the field rect OFF the pump thread.
        _activeHwnd = fg;
        _activeExe = Path.GetFileName(exePath!);
        _fieldActive = true;
        EnsureLocationHook(threadId);

        ResolveAndRaiseAsync(fg, _activeExe, ++_focusGeneration, isReanchor: false);
    }

    /// <summary>
    /// Resolve the field (classification + rect) off the pump thread with a timeout, then marshal back
    /// to raise <see cref="FieldFocused"/>. Stale results (a newer focus change has happened) are
    /// dropped via <paramref name="generation"/>.
    /// </summary>
    private void ResolveAndRaiseAsync(HWND hwnd, string exeName, int generation, bool isReanchor)
    {
        if (_targetResolver is null)
        {
            FieldFocused?.Invoke(this, new FocusedField(hwnd, exeName, null));
            return;
        }

        nint handle = hwnd;
        _ = Task.Run(() =>
        {
            FieldLocation? location = ResolveWithTimeout(handle);
            _dispatcher?.InvokeAsync(() =>
            {
                if (generation != _focusGeneration || !_fieldActive)
                {
                    return;   // superseded by a newer focus change
                }

                // On a genuine focus change a successfully-read non-editable element means "not a
                // field" → hide. On a pure re-anchor we keep the field and only update the rect, so a
                // transient non-editable/empty resolve never tears the badge down.
                DebugLog($"resolve: gen={generation} reanchor={isReanchor} exe='{exeName}' editable={location?.IsEditable} rect={location?.Rect?.ToString() ?? "null"}");

                if (!isReanchor && location is { IsEditable: false })
                {
                    Deactivate();
                    return;
                }

                FieldFocused?.Invoke(this, new FocusedField(handle, exeName, location?.Rect));
            });
        });
    }

    private FieldLocation? ResolveWithTimeout(nint hwnd)
    {
        try
        {
            var task = Task.Run(() => _targetResolver!.Resolve(hwnd));
            return task.Wait(ResolveTimeoutMs) ? task.Result : null;
        }
        catch
        {
            return null;   // resolver faulted — treat as unresolved
        }
    }

    private void EnsureLocationHook(uint threadId)
    {
        if (threadId == _activeThreadId && _locationHook is not null)
        {
            return;
        }

        _activeThreadId = threadId;
        _locationHook?.Dispose();
        _locationHook = Hook(PInvoke.EVENT_OBJECT_LOCATIONCHANGE, threadId);
    }

    private void Deactivate()
    {
        if (!_fieldActive)
        {
            return;
        }

        _fieldActive = false;
        _activeHwnd = HWND.Null;
        _activeThreadId = 0;
        _activeExe = string.Empty;
        _locationDebounce!.Stop();
        _locationHook?.Dispose();
        _locationHook = null;

        FieldUnfocused?.Invoke(this, EventArgs.Empty);
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
            if (!PInvoke.QueryFullProcessImageName(handle, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, ref size))
            {
                return null;
            }

            return new string(buffer[..(int)size]);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;   // process exited / access denied — treat as "unknown"
        }
    }

    private static string? ResolveDebugLogPath()
    {
        // On by default while we debug per-app detection; AITR_FOCUS_LOG=0 turns it off, or set a path.
        var v = Environment.GetEnvironmentVariable("AITR_FOCUS_LOG");
        if (string.Equals(v, "0", StringComparison.Ordinal))
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(v) && v != "1"
            ? v
            : Path.Combine(Path.GetTempPath(), "ai-translator-focus.log");
    }

    private static void DebugLog(string message)
    {
        if (DebugLogPath is null)
        {
            return;
        }

        try { File.AppendAllText(DebugLogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}"); }
        catch { /* diagnostics must never throw */ }
    }
}
