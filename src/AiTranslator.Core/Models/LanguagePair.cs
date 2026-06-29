namespace AiTranslator.Core.Models;

/// <summary>A pair of BCP-47 language codes the user translates between.</summary>
public sealed record LanguagePair(string Primary, string Secondary);
