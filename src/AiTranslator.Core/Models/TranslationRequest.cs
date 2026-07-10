namespace AiTranslator.Core.Models;

/// <summary>
/// Everything one translation needs: the <paramref name="Text"/> to translate, the resolved
/// <paramref name="Direction"/>, the <paramref name="Model"/>, the optional rewrite <paramref name="Style"/>,
/// and whether to apply the human-sounding (<paramref name="Humanize"/>) layer. A value object so
/// per-request options can grow without churning the streaming signature (ADR-0007).
/// </summary>
public sealed record TranslationRequest(
    string Text,
    TranslationDirection Direction,
    string Model,
    TranslationStyle Style = TranslationStyle.Original,
    bool Humanize = true);
