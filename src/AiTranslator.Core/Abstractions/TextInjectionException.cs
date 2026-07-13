namespace AiTranslator.Core.Abstractions;

/// <summary>
/// The translation could not be inserted into the target field. Thrown rather than pasting blindly:
/// injection goes through the clipboard, so if our text is not actually on it, sending Ctrl+V would
/// insert whatever was there before. The caller keeps the user's draft and reports this instead.
/// </summary>
public sealed class TextInjectionException : Exception
{
    public TextInjectionException(string message) : base(message)
    {
    }
}
