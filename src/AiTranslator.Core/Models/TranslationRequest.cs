namespace AiTranslator.Core.Models;

/// <summary>
/// Everything one translation needs: the <paramref name="Text"/> to translate, the resolved
/// <paramref name="Direction"/>, the <paramref name="Model"/>, the optional rewrite <paramref name="Style"/>,
/// whether to apply the human-sounding (<paramref name="Humanize"/>) layer, and whether the model should
/// read through typos and misheard words in the source (<paramref name="CorrectSource"/>, ADR-0010).
/// A value object so per-request options can grow without churning the streaming signature (ADR-0007).
/// </summary>
public sealed record TranslationRequest(
    string Text,
    TranslationDirection Direction,
    string Model,
    TranslationStyle Style = TranslationStyle.Original,
    bool Humanize = true,
    bool CorrectSource = false);
