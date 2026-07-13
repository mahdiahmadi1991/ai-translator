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
using AiTranslator.Core.Translation;
using AiTranslator.Infrastructure.Awareness;
using AiTranslator.Infrastructure.Input;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Velopack;
using Velopack.Sources;

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

    // Read mode: translate selected text anywhere.
    private ISelectionWatcher? _selectionWatcher;
    private SelectionBadgeWindow? _selectionBadge;
    private SelectionResultWindow? _selectionResult;
    private HotkeyService? _selectionHotkey;
    private SelectedText? _activeSelection;
    private bool _hasSelection;

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

        _ = CheckForUpdatesAsync();   // background, best-effort (no-op when not installed via Velopack)
    }

    private const string UpdateRepoUrl = "https://github.com/mahdiahmadi1991/ai-translator";

    /// <summary>
    /// Check GitHub Releases for a newer version and stage it. Applied when the user next quits, so it
    /// never interrupts. A no-op when running from source (not a Velopack install) or offline.
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var manager = new UpdateManager(new GithubSource(UpdateRepoUrl, accessToken: null, prerelease: false));
            if (!manager.IsInstalled)
            {
                return;
            }

            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                return;
            }

            await manager.DownloadUpdatesAsync(update);
            manager.WaitExitThenApplyUpdates(update, silent: false, restart: false);
            _tray?.ShowNotification(title: UiStrings.AppName, message: UiStrings.UpdateReady);
        }
        catch
        {
            // Update checks are best-effort and must never disrupt the app.
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

        _selectionHotkey = new HotkeyService(_msgSource.Handle, hotkeyId: 0xA12);
        _selectionHotkey.HotkeyPressed += (_, _) => OnSelectionHotkey();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (_hotkey.OnMessage((uint)msg, wParam) || _selectionHotkey?.OnMessage((uint)msg, wParam) == true)
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

        _selectionHotkey?.Register(_settings.SelectionHotkey);   // read-mode hotkey (best-effort)
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
            Icon = LoadAppIcon(),
            ContextMenu = menu,
        };
        _tray.ForceCreate();
    }

    /// <summary>The branded app icon (embedded as a Resource) for the tray; falls back to the system icon.</summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            var info = GetResourceStream(new Uri("pack://application:,,,/app.ico"));
            if (info is not null)
            {
                using var stream = info.Stream;
                return new Icon(stream);
            }
        }
        catch { /* fall through to the system icon */ }

        return SystemIcons.Application;
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
            _services.GetRequiredService<ISpeechRecognizer>(),
            _services.GetRequiredService<ITextCorrector>(),
            () => _settings);
        overlay.SettingsRequested += (_, _) => OpenSettings();
        overlay.StyleChanged += (exe, style) =>
        {
            // Remember the style for THIS app only, so each app keeps its own choice (ADR-0008).
            // An unidentified app updates the global default instead, so the pick is never lost.
            var updated = AppStyles.Remember(exe, style, _settings);
            try { _settingsStore.Save(updated); } catch { /* a persist failure must not break translating */ }
            _settings = updated;
        };
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

        if (_settings.SelectionTranslator)
        {
            StartSelectionWatcher();
        }
        else
        {
            StopSelectionWatcher();
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

    // ---- Read mode: translate selected text ----------------------------------------------------

    private void StartSelectionWatcher()
    {
        if (_selectionWatcher is not null)
        {
            return;
        }

        _selectionBadge = new SelectionBadgeWindow();
        _selectionBadge.Clicked += (_, _) => OnSelectionBadgeClicked();

        _selectionWatcher = new SelectionWatcher(() => _settings);
        _selectionWatcher.SelectionChanged += OnSelectionChanged;
        _selectionWatcher.SelectionCleared += OnSelectionCleared;
        _selectionWatcher.Start();
    }

    private void StopSelectionWatcher()
    {
        if (_selectionWatcher is not null)
        {
            _selectionWatcher.SelectionChanged -= OnSelectionChanged;
            _selectionWatcher.SelectionCleared -= OnSelectionCleared;
            _selectionWatcher.Dispose();
            _selectionWatcher = null;
        }

        _selectionBadge?.Close();
        _selectionBadge = null;
        _activeSelection = null;
        _hasSelection = false;
    }

    private void OnSelectionChanged(object? sender, SelectedText selection)
        => Dispatcher.InvokeAsync(() => ShowSelectionBadge(selection));

    private void OnSelectionCleared(object? sender, EventArgs e)
        => Dispatcher.InvokeAsync(HideSelectionBadge);

    private void ShowSelectionBadge(SelectedText selection)
    {
        _activeSelection = selection;
        _hasSelection = true;

        // The click that opens the pop-up doesn't clear the source app's selection, so the watcher
        // re-reads it and would re-show the icon over the pop-up. Suppress it while the pop-up is open.
        if (_selectionResult?.IsVisible == true)
        {
            _selectionBadge?.Hide();
            return;
        }

        HideBadge();   // while text is selected, the read icon takes precedence over the write badge
        if (selection.Bounds is { } rect)
        {
            bool isRtl = LanguageDirector.IsRightToLeft(selection.Text);   // anchor at the reading end
            _selectionBadge?.ShowAt(rect, isRtl);
        }
        else
        {
            _selectionBadge?.Hide();   // no bounds — the read-mode hotkey still works
        }
    }

    private void HideSelectionBadge()
    {
        _selectionBadge?.Hide();
        _activeSelection = null;
        _hasSelection = false;
    }

    private void OnSelectionBadgeClicked()
    {
        if (_activeSelection is { } selection)
        {
            _selectionBadge?.Hide();
            OpenSelectionResult(selection, selection.Bounds);
        }
    }

    private void OnSelectionHotkey()
    {
        var selection = _selectionWatcher?.CaptureCurrentSelection();
        if (selection is not null)
        {
            _selectionBadge?.Hide();   // the pop-up takes over from the icon
            OpenSelectionResult(selection, selection.Bounds);
        }
    }

    private void OpenSelectionResult(SelectedText selection, System.Drawing.Rectangle? anchor)
    {
        try
        {
            _selectionResult ??= CreateSelectionResult();
            _selectionResult.ShowFor(selection, anchor);
        }
        catch (Exception ex)
        {
            _tray?.ShowNotification(title: UiStrings.AppName, message: $"{UiStrings.OverlayError} {ex.Message}");
        }
    }

    private SelectionResultWindow CreateSelectionResult()
    {
        var window = new SelectionResultWindow(
            _services.GetRequiredService<ITranslationService>(), () => _settings);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_selectionResult, window))
            {
                _selectionResult = null;
            }
        };
        return window;
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
        if (_hasSelection)
        {
            return;   // a selection is active → the read icon takes precedence over the write badge
        }

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
            ShowOverlay(new FocusTarget(field.WindowHandle, field.ExeName), field.FieldRect);
        }
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        // A fault while building/showing the settings window must never take down the tray app — surface
        // it as a notification instead of an unhandled dispatcher exception.
        try
        {
            _settingsWindow = new SettingsWindow(_settingsStore, _secretStore);
            _settingsWindow.Saved += OnSettingsSaved;
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }
        catch (Exception ex)
        {
            _settingsWindow = null;
            _tray?.ShowNotification(title: UiStrings.AppName, message: $"{UiStrings.SettingsSaveError} {ex.Message}");
        }
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
        StopSelectionWatcher();
        _selectionResult?.Close();
        _hotkey?.Dispose();
        _selectionHotkey?.Dispose();
        _msgSource?.Dispose();
        _tray?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}
