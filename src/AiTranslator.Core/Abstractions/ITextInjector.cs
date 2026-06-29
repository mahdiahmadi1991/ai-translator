namespace AiTranslator.Core.Abstractions;

/// <summary>Writes translated text into a captured target field.</summary>
public interface ITextInjector
{
    /// <summary>Replace the entire content of the target field with <paramref name="text"/>.</summary>
    Task ReplaceTextAsync(FocusTarget target, string text, CancellationToken ct);
}
