using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private readonly ObservableCollection<string> _blocklist = new();
    private AppSettings _current;

    /// <summary>Raised after a successful save with the new settings.</summary>
    public event Action<AppSettings>? Saved;

    public SettingsWindow(ISettingsStore settingsStore, ISecretStore secretStore)
    {
        InitializeComponent();
        _settingsStore = settingsStore;
        _secretStore = secretStore;
        _current = settingsStore.Load();

        PrimaryCombo.ItemsSource = LanguageCatalog.All;
        SecondaryCombo.ItemsSource = LanguageCatalog.All;
        BlocklistItems.ItemsSource = _blocklist;
        _blocklist.CollectionChanged += (_, _) => UpdateBlocklistEmpty();
        BlocklistAddBox.TextChanged += (_, _) => UpdateWatermark();
        HotkeyBox.TextChanged += (_, _) => ValidateHotkey();

        LoadIntoFields();
        ValidateHotkey();
        UpdateBlocklistEmpty();
        UpdateWatermark();
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
        PrimaryCombo.SelectedItem = LanguageCatalog.Get(_current.LanguagePair.Primary);
        SecondaryCombo.SelectedItem = LanguageCatalog.Get(_current.LanguagePair.Secondary);
        ModelBox.Text = _current.Model;
        HotkeyBox.Text = _current.Hotkey;
        AutoDirectionBox.IsChecked = _current.AutoDirection;
        AutoAppearBadgeBox.IsChecked = _current.AutoAppearBadge;
        RunAtStartupBox.IsChecked = _current.RunAtStartup;

        _blocklist.Clear();
        foreach (var entry in _current.Blocklist)
        {
            _blocklist.Add(entry);
        }
    }

    // ---- General tab helpers ------------------------------------------------------------------

    private static string SelectedCode(ComboBox combo, string fallback)
        => combo.SelectedItem is LanguageOption option ? option.Code : fallback;

    /// <summary>Live-validate the hotkey: clear feedback + enable Save when parseable, else block Save.</summary>
    private void ValidateHotkey()
    {
        bool valid = HotkeyCombination.TryParse(HotkeyBox.Text.Trim(), out _);
        HotkeyHint.Text = valid ? string.Empty : UiStrings.SettingsHotkeyInvalid;
        SaveButton.IsEnabled = valid;

        // Save lives in the footer (visible on every tab) but the hotkey field is on General — so mirror
        // the reason there, otherwise switching tabs shows a disabled Save with no explanation.
        if (!valid)
        {
            StatusText.Foreground = ErrBrush;
            StatusText.Text = UiStrings.SettingsHotkeyInvalid;
        }
        else if (StatusText.Text == UiStrings.SettingsHotkeyInvalid)
        {
            StatusText.Text = string.Empty;   // clear only our own message, never a save result
        }
    }

    // ---- Account tab: reveal / hide the key ---------------------------------------------------

    private void OnToggleRevealKey(object sender, RoutedEventArgs e)
    {
        bool revealing = ApiKeyReveal.Visibility != Visibility.Visible;
        if (revealing)
        {
            ApiKeyReveal.Text = ApiKeyBox.Password;
            ApiKeyBox.Visibility = Visibility.Collapsed;
            ApiKeyReveal.Visibility = Visibility.Visible;
            ShowKeyButton.Content = "Hide";
        }
        else
        {
            ApiKeyBox.Password = ApiKeyReveal.Text;
            ApiKeyReveal.Visibility = Visibility.Collapsed;
            ApiKeyBox.Visibility = Visibility.Visible;
            ShowKeyButton.Content = UiStrings.SettingsApiKeyShow;
        }
    }

    private string CurrentApiKey()
        => ApiKeyReveal.Visibility == Visibility.Visible ? ApiKeyReveal.Text : ApiKeyBox.Password;

    // ---- Block List tab -----------------------------------------------------------------------

    private void OnBlocklistAddClick(object sender, RoutedEventArgs e) => AddBlocklistEntry();

    private void OnBlocklistAddKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddBlocklistEntry();
            e.Handled = true;
        }
    }

    private void AddBlocklistEntry()
    {
        string entry = BlocklistAddBox.Text.Trim();
        if (entry.Length == 0)
        {
            return;
        }

        if (!_blocklist.Any(x => string.Equals(x, entry, StringComparison.OrdinalIgnoreCase)))
        {
            _blocklist.Add(entry);
        }

        BlocklistAddBox.Clear();
        BlocklistAddBox.Focus();
    }

    private void OnBlocklistRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string entry })
        {
            _blocklist.Remove(entry);
        }
    }

    private void UpdateBlocklistEmpty()
        => BlocklistEmpty.Visibility = _blocklist.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateWatermark()
        => BlocklistAddWatermark.Visibility =
            string.IsNullOrEmpty(BlocklistAddBox.Text) ? Visibility.Visible : Visibility.Collapsed;

    // ---- Save ---------------------------------------------------------------------------------

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!HotkeyCombination.TryParse(HotkeyBox.Text.Trim(), out _))
        {
            ValidateHotkey();   // defensive: Save is disabled while invalid, but never persist a bad combo
            return;
        }

        var updated = _current with
        {
            LanguagePair = new LanguagePair(
                SelectedCode(PrimaryCombo, _current.LanguagePair.Primary),
                SelectedCode(SecondaryCombo, _current.LanguagePair.Secondary)),
            Model = ModelBox.Text.Trim(),
            Hotkey = HotkeyBox.Text.Trim(),
            AutoDirection = AutoDirectionBox.IsChecked == true,
            AutoAppearBadge = AutoAppearBadgeBox.IsChecked == true,
            RunAtStartup = RunAtStartupBox.IsChecked == true,
            Blocklist = _blocklist
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };

        // Persist + apply side effects defensively: the credential write and the HKCU Run-key write
        // can throw (interop / registry lock / policy). Surface a non-fatal message instead of letting
        // an unhandled dispatcher exception crash the app.
        try
        {
            _settingsStore.Save(updated);

            var key = CurrentApiKey();
            if (!string.IsNullOrWhiteSpace(key))
            {
                _secretStore.SetApiKey(key);
            }
            else if (_secretStore.GetApiKey() is not null)
            {
                _secretStore.DeleteApiKey();   // clearing the field removes the stored key
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
