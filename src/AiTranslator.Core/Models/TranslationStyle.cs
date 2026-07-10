namespace AiTranslator.Core.Models;

/// <summary>
/// How the AI should shape a translation beyond a faithful rendering (the write-mode "styles").
/// Exactly one applies per translation; the whole effect is produced in a single model call by
/// composing the style into the system prompt. Every style preserves the user's original meaning
/// and intent — none may invent content. See ADR-0007.
/// </summary>
public enum TranslationStyle
{
    /// <summary>Faithful, standard translation — no manipulation. The default.</summary>
    Original = 0,

    /// <summary>Polished / rephrased: clearer and more fluent, awkwardness removed.</summary>
    Professional,

    /// <summary>Formal, respectful register and sentence structure.</summary>
    Formal,

    /// <summary>Warm, casual, conversational — with a few relevant emojis.</summary>
    Friendly,

    /// <summary>Shaped as a short email: greeting, body, and a polite sign-off.</summary>
    Email,

    /// <summary>Trimmed to the essential message.</summary>
    Concise,

    /// <summary>Naturally elaborated with relevant detail, without inventing new facts.</summary>
    Expand,
}
