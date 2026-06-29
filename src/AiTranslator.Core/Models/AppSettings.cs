using System.Collections.ObjectModel;
using AiTranslator.Core.Awareness;

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

    // Regex "monikers" matched against the foreground process name (Grammarly's model). "whatsapp"
    // covers both WhatsApp.exe and the packaged WhatsApp.Root.exe; "telegram" covers Telegram.exe.
    public IReadOnlyList<string> Allowlist { get; init; } = ["whatsapp", "telegram"];
    public IReadOnlyList<string> Blocklist { get; init; } = [];

    /// <summary>Per-exe badge offset calibration (<c>appOffsets</c>); empty until the user calibrates.</summary>
    public IReadOnlyDictionary<string, AppOffset> AppOffsets { get; init; }
        = ReadOnlyDictionary<string, AppOffset>.Empty;

    public string Theme { get; init; } = "system";
    public string UiLanguage { get; init; } = "fa";
    public bool RunAtStartup { get; init; } = false;
    public bool AutoSend { get; init; } = false;

    public static AppSettings Default { get; } = new();
}
