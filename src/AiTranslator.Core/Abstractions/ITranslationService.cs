using AiTranslator.Core.Models;

namespace AiTranslator.Core.Abstractions;

/// <summary>Streams a translation. Implementations isolate all provider-specific types.</summary>
public interface ITranslationService
{
    /// <summary>Streams the translation as incremental text chunks. Honors cancellation.</summary>
    IAsyncEnumerable<string> TranslateStreamAsync(
        string text, TranslationDirection direction, string model, CancellationToken ct);
}
