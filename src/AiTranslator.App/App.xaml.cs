using System.Drawing;                 // SystemIcons (placeholder tray icon until M4 branding)
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using AiTranslator.App.Composition;
using AiTranslator.App.Resources;
using AiTranslator.App.Windows;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Models;
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

    private void ShowOverlay()
    {
        // Recreate per invocation so the latest settings apply and the target is freshly captured.
        _overlay?.Close();
        _overlay = new OverlayInputWindow(
            _services.GetRequiredService<IFocusTargetProvider>(),
            _services.GetRequiredService<ITranslationService>(),
            _services.GetRequiredService<ITextInjector>(),
            _settings);
        _overlay.ShowFor();
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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _msgSource?.Dispose();
        _tray?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}
