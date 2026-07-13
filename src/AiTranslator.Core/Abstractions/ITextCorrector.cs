namespace AiTranslator.Core.Abstractions;

/// <summary>
/// Proof-reads the text in the compose box before it is used: fixes typos, repairs words the
/// speech-to-text engine misheard, and restores English terms that were transliterated into another
/// script (ADR-0010). It corrects spelling, never style, and never translates.
/// </summary>
public interface ITextCorrector
{
    /// <summary>
    /// Returns the corrected text. Implementations must return the input unchanged rather than throw
    /// when there is nothing to correct; callers treat a failure as "keep what the user has".
    /// </summary>
    Task<string> CorrectAsync(string text, string model, CancellationToken ct = default);
}
