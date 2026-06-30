using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using AiTranslator.App.Resources;
using AiTranslator.App.Shell;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Input;
using AiTranslator.Core.Models;

namespace AiTranslator.App.Windows;

/// <summary>
/// Edits non-secret settings and the OpenAI key. Auto-saves: every change is persisted immediately
/// (text fields on focus-loss / window close) and <see cref="Saved"/> is raised so the host applies it
/// live — no Save button, no "saved" confirmation. Built on WPF-UI (Fluent) controls.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ISettingsStore _settingsStore;
    private readonly ISecretStore _secretStore;
    private readonly ObservableCollection<string> _blocklist = new();
    private AppSettings _current;
    private bool _loading;

    /// <summary>Raised after each successful save with the new settings (host re-applies hotkey/badge).</summary>
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

        LoadIntoFields();
        WireAutoSave();
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

    protected override void OnClosing(CancelEventArgs e)
    {
        // Flush edits that only persist on focus-loss (text fields) in case the window is closed first.
        PersistSettings();
        PersistApiKey();
        base.OnClosing(e);
    }

    private void LoadIntoFields()
    {
        _loading = true;
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

        UpdateBlocklistEmpty();
        ValidateHotkey();
        _loading = false;
    }

    private void WireAutoSave()
    {
        // Toggles + dropdowns: persist immediately.
        foreach (var toggle in new[] { AutoDirectionBox, AutoAppearBadgeBox, RunAtStartupBox })
        {
            toggle.Checked += OnSettingChanged;
            toggle.Unchecked += OnSettingChanged;
        }

        PrimaryCombo.SelectionChanged += (_, _) => PersistSettings();
        SecondaryCombo.SelectionChanged += (_, _) => PersistSettings();

        // Text fields: validate live, persist on focus-loss (avoids per-keystroke churn / partial values).
        HotkeyBox.TextChanged += (_, _) => ValidateHotkey();
        HotkeyBox.LostFocus += (_, _) => PersistSettings();
        ModelBox.LostFocus += (_, _) => PersistSettings();
        ApiKeyBox.LostFocus += (_, _) => PersistApiKey();

        // Block list changes (add/remove) persist immediately.
        _blocklist.CollectionChanged += (_, _) =>
        {
            UpdateBlocklistEmpty();
            PersistSettings();
        };
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e) => PersistSettings();

    // ---- Persistence --------------------------------------------------------------------------

    private void PersistSettings()
    {
        if (_loading)
        {
            return;
        }

        bool hotkeyValid = HotkeyCombination.TryParse(HotkeyBox.Text.Trim(), out _);

        var updated = _current with
        {
            LanguagePair = new LanguagePair(
                SelectedCode(PrimaryCombo, _current.LanguagePair.Primary),
                SelectedCode(SecondaryCombo, _current.LanguagePair.Secondary)),
            Model = string.IsNullOrWhiteSpace(ModelBox.Text) ? _current.Model : ModelBox.Text.Trim(),
            Hotkey = hotkeyValid ? HotkeyBox.Text.Trim() : _current.Hotkey,   // never persist a bad combo
            AutoDirection = AutoDirectionBox.IsChecked == true,
            AutoAppearBadge = AutoAppearBadgeBox.IsChecked == true,
            RunAtStartup = RunAtStartupBox.IsChecked == true,
            Blocklist = _blocklist
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };

        try
        {
            _settingsStore.Save(updated);
            StartupManager.Apply(updated.RunAtStartup);
        }
        catch (Exception ex)
        {
            ShowError($"{UiStrings.SettingsSaveError} {ex.Message}");
            return;
        }

        _current = updated;
        ClearError();
        Saved?.Invoke(updated);
    }

    private void PersistApiKey()
    {
        if (_loading)
        {
            return;
        }

        try
        {
            var key = ApiKeyBox.Password;
            if (!string.IsNullOrWhiteSpace(key))
            {
                _secretStore.SetApiKey(key);
            }
            else if (_secretStore.GetApiKey() is not null)
            {
                _secretStore.DeleteApiKey();   // clearing the field removes the stored key
            }

            ClearError();
        }
        catch (Exception ex)
        {
            ShowError($"{UiStrings.SettingsSaveError} {ex.Message}");
        }
    }

    private static string SelectedCode(ComboBox combo, string fallback)
        => combo.SelectedItem is LanguageOption option ? option.Code : fallback;

    /// <summary>Live-validate the hotkey and show an inline reason; an invalid combo just isn't saved.</summary>
    private void ValidateHotkey()
    {
        bool valid = HotkeyCombination.TryParse(HotkeyBox.Text.Trim(), out _);
        HotkeyHint.Text = valid ? string.Empty : UiStrings.SettingsHotkeyInvalid;
        HotkeyHint.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- Block list ---------------------------------------------------------------------------

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
            _blocklist.Add(entry);   // CollectionChanged persists
        }

        BlocklistAddBox.Text = string.Empty;
        BlocklistAddBox.Focus();
    }

    private void OnBlocklistRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string entry })
        {
            _blocklist.Remove(entry);   // CollectionChanged persists
        }
    }

    private void UpdateBlocklistEmpty()
        => BlocklistEmpty.Visibility = _blocklist.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ---- Error surface (auto-save has no status line; only failures are shown) -----------------

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = string.Empty;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int pvAttribute, int cbAttribute);
}
