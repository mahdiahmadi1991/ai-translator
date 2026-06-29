using System.Drawing;                 // SystemIcons (placeholder tray icon until M4 branding)
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using AiTranslator.App.Composition;
using AiTranslator.App.Resources;
using AiTranslator.App.Windows;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Awareness;
using AiTranslator.Core.Models;
using AiTranslator.Infrastructure.Awareness;
using AiTranslator.Infrastructure.Input;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;

namespace AiTranslator.App;

/// <summary>Tray application entry point: wires DI, the global hotkey, and the windows.</summary>
public partial class App : Application
{
    private const int HwndMessage = -3;   // message-only window parent

    private ServiceProvider _services = null!;
    private ISettingsStore _settingsStore = null!;
    private ISecretStore _secretStore = null!;
    private HwndSource _msgSource = null!;
    private HotkeyService _hotkey = null!;
    private TaskbarIcon _tray = null!;
    private OverlayInputWindow? _overlay;
    private SettingsWindow? _settingsWindow;
    private IFocusWatcher? _focusWatcher;
    private BadgeWindow? _badge;
    private FocusedField? _activeField;
    private nint _overlayTargetHwnd;
    private AppSettings _settings = AppSettings.Default;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;   // tray app: stay alive when windows close

        _services = ServiceConfiguration.Build();
        _settingsStore = _services.GetRequiredService<ISettingsStore>();
        _secretStore = _services.GetRequiredService<ISecretStore>();
        _settings = _settingsStore.Load();

        CreateMessageWindowAndHotkey();
        RegisterHotkey();
        CreateTrayIcon();
        SyncAwareness();   // start the Grammarly-style badge watcher if enabled (M2)

        if (_secretStore.GetApiKey() is null)
        {
            OpenSettings();   // first run — capture the API key before use
        }
    }

    private void CreateMessageWindowAndHotkey()
    {
        var parameters = new HwndSourceParameters("AiTranslatorHotkeyWindow")
        {
            ParentWindow = new nint(HwndMessage),
            WindowStyle = 0,
        };
        _msgSource = new HwndSource(parameters);
        _msgSource.AddHook(WndProc);

        _hotkey = new HotkeyService(_msgSource.Handle);
        _hotkey.HotkeyPressed += (_, _) => ShowOverlay();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (_hotkey.OnMessage((uint)msg, wParam))
        {
            handled = true;
        }

        return nint.Zero;
    }

    private void RegisterHotkey()
    {
        if (!_hotkey.Register(_settings.Hotkey))
        {
            // Real, actionable state (not a stub): tell the user to pick a free combo in Settings.
            _tray?.ShowNotification(title: UiStrings.AppName, message: UiStrings.SettingsHotkeyTaken);
        }
    }

    private void CreateTrayIcon()
    {
        var menu = new ContextMenu();
        var settingsItem = new MenuItem { Header = UiStrings.TraySettings };
        settingsItem.Click += (_, _) => OpenSettings();
        var exitItem = new MenuItem { Header = UiStrings.TrayExit };
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(settingsItem);
        menu.Items.Add(exitItem);

        _tray = new TaskbarIcon
        {
            ToolTipText = UiStrings.TrayTooltip,
            Icon = SystemIcons.Application,   // TODO(quality): replace with a branded .ico (M4)
            ContextMenu = menu,
        };
        _tray.ForceCreate();
    }

    private void ShowOverlay(FocusTarget? target = null, System.Drawing.Rectangle? anchor = null)
    {
        // Recreate per invocation so the latest settings apply and the target is freshly captured.
        CloseOverlay();
        _overlayTargetHwnd = target?.WindowHandle ?? 0;

        var overlay = new OverlayInputWindow(
            _services.GetRequiredService<IFocusTargetProvider>(),
            _services.GetRequiredService<ITranslationService>(),
            _services.GetRequiredService<ITextInjector>(),
            _settings);
        _overlay = overlay;
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(_overlay, overlay))
            {
                _overlay = null;
                _overlayTargetHwnd = 0;
            }
        };

        if (target is { } resolved)
        {
            overlay.ShowFor(resolved, anchor);   // badge path: type into the watcher-resolved field
        }
        else
        {
            overlay.ShowFor();                   // hotkey path: capture the foreground window
        }
    }

    private void CloseOverlay()
    {
        _overlay?.Close();
        _overlay = null;
        _overlayTargetHwnd = 0;
    }

    // ---- M2 awareness: badge auto-appearance --------------------------------------------------

    /// <summary>Start or stop the focus watcher to match the current <c>AutoAppearBadge</c> setting.</summary>
    private void SyncAwareness()
    {
        if (_settings.AutoAppearBadge)
        {
            StartAwareness();
        }
        else
        {
            StopAwareness();
        }
    }

    private void StartAwareness()
    {
        if (_focusWatcher is not null)
        {
            return;   // already running
        }

        _badge = new BadgeWindow();
        _badge.Clicked += (_, _) => OnBadgeClicked();

        _focusWatcher = new FocusWatcher(() => _settings, _services.GetRequiredService<ITargetResolver>());
        _focusWatcher.FieldFocused += OnFieldFocused;
        _focusWatcher.FieldUnfocused += OnFieldUnfocused;
        _focusWatcher.Start();
    }

    private void StopAwareness()
    {
        if (_focusWatcher is not null)
        {
            _focusWatcher.FieldFocused -= OnFieldFocused;
            _focusWatcher.FieldUnfocused -= OnFieldUnfocused;
            _focusWatcher.Dispose();
            _focusWatcher = null;
        }

        _badge?.Close();
        _badge = null;
        _activeField = null;
    }

    // The watcher raises events on its own STA thread — marshal to the UI thread. Use the NON-blocking
    // InvokeAsync so the watcher thread never waits on the UI thread (a blocking Invoke would deadlock
    // for ~2s against StopAwareness()'s Join during app exit / settings toggle).
    private void OnFieldFocused(object? sender, FocusedField field) => Dispatcher.InvokeAsync(() => ShowBadge(field));

    // Focus left every watched field (and it is not our overlay) → dismiss the badge AND the box.
    private void OnFieldUnfocused(object? sender, EventArgs e) => Dispatcher.InvokeAsync(() =>
    {
        HideBadge();
        CloseOverlay();
    });

    private void ShowBadge(FocusedField field)
    {
        // A different field gained focus while a box was open for the previous one → dismiss the box.
        if (_overlay is not null && _overlayTargetHwnd != field.WindowHandle)
        {
            CloseOverlay();
        }

        _activeField = field;
        if (field.FieldRect is { } rect)
        {
            _badge?.ShowAt(rect, AppOffsets.For(field.ExeName, _settings));
        }
        else
        {
            _badge?.Hide();   // editable but no bounds — avoid a mis-placed badge; hotkey still works
        }
    }

    private void HideBadge()
    {
        _badge?.Hide();
        _activeField = null;
    }

    private void OnBadgeClicked()
    {
        if (_activeField is { } field)
        {
            ShowOverlay(new FocusTarget(field.WindowHandle), field.FieldRect);
        }
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settingsStore, _secretStore);
        _settingsWindow.Saved += OnSettingsSaved;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnSettingsSaved(AppSettings updated)
    {
        _settings = updated;
        RegisterHotkey();   // re-register in case the hotkey changed
        SyncAwareness();    // start/stop the badge watcher if AutoAppearBadge changed
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StopAwareness();
        _hotkey?.Dispose();
        _msgSource?.Dispose();
        _tray?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}
