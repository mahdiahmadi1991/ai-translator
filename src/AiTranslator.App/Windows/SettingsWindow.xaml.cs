using System.Windows;
using AiTranslator.App.Resources;
using AiTranslator.App.Shell;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Models;

namespace AiTranslator.App.Windows;

/// <summary>Edits non-secret settings and the OpenAI key. Raises <see cref="Saved"/> so the host re-registers the hotkey.</summary>
public partial class SettingsWindow : Window
{
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
        LoadIntoFields();
    }

    private void LoadIntoFields()
    {
        ApiKeyBox.Password = _secretStore.GetApiKey() ?? string.Empty;
        PrimaryBox.Text = _current.LanguagePair.Primary;
        SecondaryBox.Text = _current.LanguagePair.Secondary;
        ModelBox.Text = _current.Model;
        HotkeyBox.Text = _current.Hotkey;
        AutoDirectionBox.IsChecked = _current.AutoDirection;
        RunAtStartupBox.IsChecked = _current.RunAtStartup;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var updated = _current with
        {
            LanguagePair = new LanguagePair(PrimaryBox.Text.Trim(), SecondaryBox.Text.Trim()),
            Model = ModelBox.Text.Trim(),
            Hotkey = HotkeyBox.Text.Trim(),
            AutoDirection = AutoDirectionBox.IsChecked == true,
            RunAtStartup = RunAtStartupBox.IsChecked == true,
        };

        _settingsStore.Save(updated);

        var key = ApiKeyBox.Password;
        if (!string.IsNullOrWhiteSpace(key))
        {
            _secretStore.SetApiKey(key);
        }

        StartupManager.Apply(updated.RunAtStartup);

        _current = updated;
        StatusText.Text = UiStrings.SettingsSaved;
        Saved?.Invoke(updated);
    }
}
