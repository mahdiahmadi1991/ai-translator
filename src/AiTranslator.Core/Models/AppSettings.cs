namespace AiTranslator.Core.Models;

/// <summary>
/// All non-secret, user-configurable settings. Persisted as JSON; the OpenAI key is NOT here
/// (it lives in Windows Credential Manager). Field names mirror docs/reference/configuration.md.
/// </summary>
public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public LanguagePair LanguagePair { get; init; } = new("fa", "en");
    public bool AutoDirection { get; init; } = true;
    public string Model { get; init; } = "gpt-5.1";
    public int DebounceMs { get; init; } = 500;
    public string Hotkey { get; init; } = "Ctrl+Alt+T";
    public bool AutoAppearBadge { get; init; } = true;
    public IReadOnlyList<string> Allowlist { get; init; } = ["WhatsApp.exe", "Telegram.exe"];
    public IReadOnlyList<string> Blocklist { get; init; } = [];
    public string Theme { get; init; } = "system";
    public string UiLanguage { get; init; } = "fa";
    public bool RunAtStartup { get; init; } = false;
    public bool AutoSend { get; init; } = false;

    public static AppSettings Default { get; } = new();
}
