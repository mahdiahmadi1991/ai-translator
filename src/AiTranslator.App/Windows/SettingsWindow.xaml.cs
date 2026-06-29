using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AiTranslator.App.Resources;
using AiTranslator.App.Shell;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Input;
using AiTranslator.Core.Models;

namespace AiTranslator.App.Windows;

/// <summary>Edits non-secret settings and the OpenAI key. Raises <see cref="Saved"/> so the host re-registers the hotkey.</summary>
public partial class SettingsWindow : Window
{
    private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly Brush ErrBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x75));

    private readonly ISettingsStore _settingsStore;
    private readonly ISecretStore _secretStore;
    private AppSettings _current;

    /// <summary>Raised after a successful save with the new settings.</summary>
    public event Action<AppSettings>? Saved;

    public SettingsWindow(ISettingsStore settingsStore, ISecretStore secretStore)
    {
        InitializeComponent();
        _settingsStore = settingsStore;
        _secretStore = secretStore;
        _current = settingsStore.Load();
        HotkeyBox.TextChanged += (_, _) => ValidateHotkey();
        LoadIntoFields();
        ValidateHotkey();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            nint hwnd = new WindowInteropHelper(this).Handle;
            int enabled = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
        }
        catch { /* dark title bar is cosmetic — never fail the window over it */ }
    }

    private void LoadIntoFields()
    {
        ApiKeyBox.Password = _secretStore.GetApiKey() ?? string.Empty;
        PrimaryBox.Text = _current.LanguagePair.Primary;
        SecondaryBox.Text = _current.LanguagePair.Secondary;
        ModelBox.Text = _current.Model;
        HotkeyBox.Text = _current.Hotkey;
        AutoDirectionBox.IsChecked = _current.AutoDirection;
        AutoAppearBadgeBox.IsChecked = _current.AutoAppearBadge;
        RunAtStartupBox.IsChecked = _current.RunAtStartup;
        AllowlistBox.Text = string.Join(Environment.NewLine, _current.Allowlist);
        BlocklistBox.Text = string.Join(Environment.NewLine, _current.Blocklist);
    }

    /// <summary>Live-validate the hotkey: clear feedback + enable Save when parseable, else block Save.</summary>
    private void ValidateHotkey()
    {
        bool valid = HotkeyCombination.TryParse(HotkeyBox.Text.Trim(), out _);
        HotkeyHint.Text = valid ? string.Empty : UiStrings.SettingsHotkeyInvalid;
        SaveButton.IsEnabled = valid;
    }

    private static IReadOnlyList<string> ParseLines(string text)
        => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!HotkeyCombination.TryParse(HotkeyBox.Text.Trim(), out _))
        {
            ValidateHotkey();   // defensive: Save is disabled while invalid, but never persist a bad combo
            return;
        }

        var updated = _current with
        {
            LanguagePair = new LanguagePair(PrimaryBox.Text.Trim(), SecondaryBox.Text.Trim()),
            Model = ModelBox.Text.Trim(),
            Hotkey = HotkeyBox.Text.Trim(),
            AutoDirection = AutoDirectionBox.IsChecked == true,
            AutoAppearBadge = AutoAppearBadgeBox.IsChecked == true,
            RunAtStartup = RunAtStartupBox.IsChecked == true,
            Allowlist = ParseLines(AllowlistBox.Text),
            Blocklist = ParseLines(BlocklistBox.Text),
        };

        // Persist + apply side effects defensively: the credential write and the HKCU Run-key write
        // can throw (interop / registry lock / policy). Surface a non-fatal message instead of letting
        // an unhandled dispatcher exception crash the app.
        try
        {
            _settingsStore.Save(updated);

            var key = ApiKeyBox.Password;
            if (!string.IsNullOrWhiteSpace(key))
            {
                _secretStore.SetApiKey(key);
            }

            StartupManager.Apply(updated.RunAtStartup);
        }
        catch (Exception ex)
        {
            StatusText.Foreground = ErrBrush;
            StatusText.Text = $"{UiStrings.SettingsSaveError} {ex.Message}";
            return;
        }

        _current = updated;
        StatusText.Foreground = OkBrush;
        StatusText.Text = "✓ " + UiStrings.SettingsSaved;
        Saved?.Invoke(updated);
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int pvAttribute, int cbAttribute);
}
