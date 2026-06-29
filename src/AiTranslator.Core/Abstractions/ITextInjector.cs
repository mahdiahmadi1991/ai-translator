namespace AiTranslator.Core.Abstractions;

/// <summary>Writes translated text into a captured target field.</summary>
public interface ITextInjector
{
    /// <summary>Append <paramref name="text"/> at the end of the target field's current content
    /// (existing text is preserved; the caret ends at the end).</summary>
    Task AppendTextAsync(FocusTarget target, string text, CancellationToken ct);
}
