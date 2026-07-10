using AiTranslator.Core.Models;
using Wpf.Ui.Controls;

namespace AiTranslator.App.Resources;

/// <summary>A selectable rewrite style: the enum value, its display name, an icon, and a one-line hint.</summary>
public sealed record RewriteStyleOption(TranslationStyle Style, string Name, SymbolRegular Icon, string Hint);

/// <summary>
/// The rewrite styles offered in the compose box footer (order = display order). Names/hints are the
/// app's English source strings (localized later like the rest of the UI); the mapping to the prompt
/// lives in <see cref="AiTranslator.Core.Translation.PromptBuilder"/>.
/// </summary>
public static class RewriteStyleCatalog
{
    public static IReadOnlyList<RewriteStyleOption> All { get; } =
    [
        new(TranslationStyle.Original, "Original", SymbolRegular.TextParagraph24,
            "Faithful translation, no changes."),
        new(TranslationStyle.Professional, "Professional", SymbolRegular.Sparkle24,
            "Polished and more fluent."),
        new(TranslationStyle.Formal, "Formal", SymbolRegular.Briefcase24,
            "Formal, respectful register."),
        new(TranslationStyle.Friendly, "Friendly", SymbolRegular.Emoji24,
            "Warm and casual, with emojis."),
        new(TranslationStyle.Email, "Email", SymbolRegular.Mail24,
            "Greeting, body, and sign-off."),
        new(TranslationStyle.Concise, "Concise", SymbolRegular.TextGrammarArrowLeft24,
            "Trimmed to the essentials."),
        new(TranslationStyle.Expand, "Expand", SymbolRegular.ArrowExpand24,
            "Fuller, more detailed phrasing."),
    ];

    public static RewriteStyleOption Get(TranslationStyle style)
    {
        foreach (var option in All)
        {
            if (option.Style == style)
            {
                return option;
            }
        }

        return All[0];   // fall back to Original
    }
}
