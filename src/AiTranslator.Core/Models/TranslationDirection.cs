namespace AiTranslator.Core.Models;

/// <summary>The resolved source/target language codes for one translation request.</summary>
public readonly record struct TranslationDirection(string SourceLang, string TargetLang);
