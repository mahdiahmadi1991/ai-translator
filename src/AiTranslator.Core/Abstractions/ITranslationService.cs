using AiTranslator.Core.Models;

namespace AiTranslator.Core.Abstractions;

/// <summary>Streams a translation. Implementations isolate all provider-specific types.</summary>
public interface ITranslationService
{
    /// <summary>Streams the translation as incremental text chunks. Honors cancellation. The
    /// <see cref="TranslationRequest"/> carries the text, direction, model, rewrite style, and the
    /// human-sounding flag so the whole effect is produced in a single model call (ADR-0007).</summary>
    IAsyncEnumerable<string> TranslateStreamAsync(TranslationRequest request, CancellationToken ct);
}
