using AiTranslator.Core.Models;

namespace AiTranslator.Core.Translation;

/// <summary>Decides which way to translate based on the input's dominant script.</summary>
public static class LanguageDirector
{
    // Languages written in Arabic/Persian script. Extend as the supported set grows.
    private static readonly HashSet<string> ArabicScriptLangs = new(StringComparer.OrdinalIgnoreCase)
    {
        "fa", "ar", "ur", "ps",
    };

    public static TranslationDirection Resolve(string text, LanguagePair pair, bool autoDirection)
    {
        if (!autoDirection || string.IsNullOrWhiteSpace(text))
        {
            return new TranslationDirection(pair.Primary, pair.Secondary);
        }

        bool inputIsArabicScript = IsMostlyArabicScript(text);
        bool primaryIsArabicScript = ArabicScriptLangs.Contains(pair.Primary);

        // If the input's script matches the primary language's script, source = primary.
        bool sourceIsPrimary = inputIsArabicScript == primaryIsArabicScript;
        return sourceIsPrimary
            ? new TranslationDirection(pair.Primary, pair.Secondary)
            : new TranslationDirection(pair.Secondary, pair.Primary);
    }

    private static bool IsMostlyArabicScript(string text)
    {
        int arabic = 0, latin = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            // Arabic block U+0600–U+06FF plus Arabic supplement / presentation forms used by Persian.
            if (rune.Value is (>= 0x0600 and <= 0x06FF)
                or (>= 0x0750 and <= 0x077F)
                or (>= 0xFB50 and <= 0xFDFF)
                or (>= 0xFE70 and <= 0xFEFF))
            {
                arabic++;
            }
            else if (rune.Value < 0x0250 && rune.Value > 0x40
                && char.IsLetter((char)rune.Value))
            {
                latin++;
            }
        }

        return arabic >= latin;
    }
}
