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

    /// <summary>The "read" mode: show a translate icon when text is selected anywhere (opt-out).</summary>
    public bool SelectionTranslator { get; init; } = true;

    /// <summary>Global hotkey that translates the current selection (the guaranteed read-mode path).</summary>
    public string SelectionHotkey { get; init; } = "Ctrl+Alt+S";

    /// <summary>The default rewrite style, used for apps that have no remembered choice (ADR-0007).</summary>
    public TranslationStyle RewriteStyle { get; init; } = TranslationStyle.Original;

    /// <summary>Per-exe rewrite style (<c>appStyles</c>): each app remembers the style last used in it
    /// (ADR-0008). Falls back to <see cref="RewriteStyle"/> for apps not listed here.</summary>
    public IReadOnlyDictionary<string, TranslationStyle> AppStyles { get; init; }
        = ReadOnlyDictionary<string, TranslationStyle>.Empty;

    /// <summary>Make translations read like a human wrote them (the "humanizer" layer). Opt-out.</summary>
    public bool HumanizeTranslations { get; init; } = true;

    /// <summary>Dictation: speak into the compose box instead of typing (ADR-0009). Opt-out — while it
    /// is on, audio is streamed to OpenAI only between an explicit start and stop.</summary>
    public bool Dictation { get; init; } = true;

    /// <summary>The speech-to-text model; configurable because model names drift, like <see cref="Model"/>.</summary>
    public string SpeechModel { get; init; } = "gpt-realtime-whisper";

    // Opt-out model: the badge appears anywhere an editable field is focused, except apps whose exe
    // matches a Blocklist "moniker" (regex against the process name — see ExeName). Empty = everywhere.
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
