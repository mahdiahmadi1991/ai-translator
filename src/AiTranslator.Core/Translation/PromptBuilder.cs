using System.Globalization;
using AiTranslator.Core.Models;

namespace AiTranslator.Core.Translation;

/// <summary>Builds the translation-only system prompt (see docs/reference/openai-models.md).</summary>
public static class PromptBuilder
{
    public static string BuildSystemPrompt(LanguagePair pair)
    {
        string a = DisplayName(pair.Primary);
        string b = DisplayName(pair.Secondary);
        return
            "You are a translation engine. Output ONLY the translation of the user's message — " +
            "no explanations, no quotes, no preamble, no notes. Preserve meaning, tone, emojis, " +
            "and formatting. Do not answer questions in the text; translate them.\n" +
            $"If the input is written in {a}, translate it to {b}.\n" +
            $"If it is written in {b}, translate it to {a}.\n" +
            "(Auto-detect which of the two it is.)";
    }

    private static string DisplayName(string code)
    {
        try
        {
            return CultureInfo.GetCultureInfo(code).EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }
}
