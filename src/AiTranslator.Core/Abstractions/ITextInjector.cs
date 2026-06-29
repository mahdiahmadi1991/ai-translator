namespace AiTranslator.Core.Abstractions;

/// <summary>Writes translated text into a captured target field.</summary>
public interface ITextInjector
{
    /// <summary>Append <paramref name="text"/> at the end of the target field's current content
    /// (existing text is preserved).</summary>
    Task AppendTextAsync(FocusTarget target, string text, CancellationToken ct);

    /// <summary>Move the target field's caret to the end of its text (after an injection).</summary>
    void PlaceCaretAtEnd(FocusTarget target);
}
