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
        // Reuse a single box so an in-progress draft survives hide/show (it reads live settings via a
        // provider). It hides — not closes — when focus leaves, then reappears with the same text.
        _overlay ??= CreateOverlay();

        if (target is { } resolved)
        {
            _overlay.ShowFor(resolved, anchor);   // badge path: type into the watcher-resolved field
        }
        else
        {
            _overlay.ShowFor();                   // hotkey path: capture the foreground window
        }
    }

    private OverlayInputWindow CreateOverlay()
    {
        var overlay = new OverlayInputWindow(
            _services.GetRequiredService<IFocusTargetProvider>(),
            _services.GetRequiredService<ITranslationService>(),
            _services.GetRequiredService<ITextInjector>(),
            () => _settings);
        overlay.SettingsRequested += (_, _) => OpenSettings();
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(_overlay, overlay))
            {
                _overlay = null;
            }
        };
        return overlay;
    }

    private void CloseOverlay()
    {
        _overlay?.Close();
        _overlay = null;
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
        _badge.SettingsRequested += (_, _) => OpenSettings();
        _badge.QuitRequested += (_, _) => Shutdown();
        _badge.IgnoreAppRequested += (_, _) => IgnoreCurrentApp();

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

    // Focus left every watched field → hide the badge. (The overlay dismisses itself when focus
    // truly leaves it — see OverlayInputWindow — so injection's focus churn never closes it.)
    private void OnFieldUnfocused(object? sender, EventArgs e) => Dispatcher.InvokeAsync(HideBadge);

    private void ShowBadge(FocusedField field)
    {
        _activeField = field;
        _badge?.SetAppName(field.ExeName);   // label the right-click "don't show in <app>" item
        if (field.FieldRect is { } rect)
        {
            _badge?.ShowAt(rect, AppOffsets.For(field.ExeName, _settings));
        }
        else
        {
            _badge?.Hide();   // editable but no bounds — avoid a mis-placed badge; hotkey still works
        }
    }

    /// <summary>Right-click → "don't show here": add the current app to the blocklist and dismiss.</summary>
    private void IgnoreCurrentApp()
    {
        if (_activeField is not { } field || string.IsNullOrWhiteSpace(field.ExeName))
        {
            return;
        }

        if (!_settings.Blocklist.Contains(field.ExeName))
        {
            var updated = _settings with { Blocklist = [.. _settings.Blocklist, field.ExeName] };
            _settingsStore.Save(updated);
            _settings = updated;   // the watcher reads this live, so the badge won't return here
        }

        HideBadge();
        CloseOverlay();
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
            _badge?.Hide();   // the box replaces the badge; the badge reappears when the field refocuses
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
        CloseOverlay();
        StopAwareness();
        _hotkey?.Dispose();
        _msgSource?.Dispose();
        _tray?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}
