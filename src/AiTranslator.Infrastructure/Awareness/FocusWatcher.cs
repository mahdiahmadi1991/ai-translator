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
/// thread, which must pump messages). Events are raised on that thread — consumers marshal to the UI
/// thread themselves. The global hotkey remains the guaranteed path; auto-appearance is best-effort.
/// </summary>
public sealed class FocusWatcher : IFocusWatcher
{
    private const int OBJID_CURSOR = -9;                 // skip mouse-cursor object noise on FOCUS
    private const int LocationDebounceMs = 80;           // coalesce LOCATIONCHANGE bursts

    private readonly Func<AppSettings> _settingsProvider;
    private readonly ITargetResolver? _targetResolver;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    private Thread? _thread;
    private Dispatcher? _dispatcher;
    private DispatcherTimer? _locationDebounce;
    private WINEVENTPROC? _proc;                          // rooted so the GC never moves/collects it
    private UnhookWinEventSafeHandle? _foregroundHook;
    private UnhookWinEventSafeHandle? _focusHook;
    private UnhookWinEventSafeHandle? _locationHook;      // scoped to the active field's thread

    private HWND _activeHwnd;
    private uint _activeThreadId;
    private bool _fieldActive;

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
        var dispatcher = _dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.InvokeShutdown();   // ends Dispatcher.Run(); the hook thread then disposes its hooks
        _thread?.Join(millisecondsTimeout: 2000);
        _thread = null;
        _dispatcher = null;
    }

    public void Dispose() => Stop();

    private void RunMessageLoop()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _proc = OnWinEvent;
        _locationDebounce = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(LocationDebounceMs),
        };
        _locationDebounce.Tick += OnLocationDebounceTick;

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
        if (hwnd.IsNull)
        {
            return;
        }

        if (@event == PInvoke.EVENT_OBJECT_LOCATIONCHANGE)
        {
            // The active field (or its window) moved/resized — re-anchor after a short debounce.
            if (_fieldActive && hwnd == _activeHwnd && _targetResolver is not null)
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

        HandleFocusChange(hwnd);
    }

    private void OnLocationDebounceTick(object? sender, EventArgs e)
    {
        _locationDebounce!.Stop();
        if (_fieldActive && !_activeHwnd.IsNull)
        {
            HandleFocusChange(_activeHwnd);
        }
    }

    private void HandleFocusChange(HWND hwnd)
    {
        uint threadId = PInvoke.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == _ownProcessId)
        {
            return;   // our own windows / unknown — leave current state untouched
        }

        string? exePath = ResolveExe(pid);
        var settings = _settingsProvider();
        if (exePath is null || !AppActivationPolicy.ShouldActivate(exePath, settings.Allowlist, settings.Blocklist))
        {
            Deactivate();
            return;
        }

        FieldLocation? location = _targetResolver?.Resolve(hwnd);
        if (_targetResolver is not null && location is { IsEditable: false })
        {
            Deactivate();   // allowlisted app, but the focused element is not an editable field
            return;
        }

        _activeHwnd = hwnd;
        _fieldActive = true;
        EnsureLocationHook(threadId);

        FieldFocused?.Invoke(this, new FocusedField(hwnd, Path.GetFileName(exePath), location?.Rect));
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
}
