using System.Globalization;
using System.Text;
using AiTranslator.Core.Models;

namespace AiTranslator.Core.Translation;

/// <summary>
/// Builds the translation system prompt by composing three layers in one call (ADR-0007):
/// a strict translation base, an optional rewrite <see cref="TranslationStyle"/>, and an optional
/// human-sounding ("humanizer") layer. All prompt text is English (the app's source language);
/// user-facing style names are localized in the UI, not here.
/// </summary>
public static class PromptBuilder
{
    public static string BuildSystemPrompt(LanguagePair pair)
        => BuildSystemPrompt(pair, TranslationStyle.Original, humanize: false);

    public static string BuildSystemPrompt(LanguagePair pair, TranslationStyle style, bool humanize)
    {
        string a = DisplayName(pair.Primary);
        string b = DisplayName(pair.Secondary);

        var sb = new StringBuilder();
        sb.Append(
            "You are a translation engine. Output ONLY the translation of the user's message. " +
            "No explanations, no quotes, no preamble, no notes. Do not answer questions in the text; " +
            "translate them.\n");
        sb.Append($"If the input is written in {a}, translate it to {b}.\n");
        sb.Append($"If it is written in {b}, translate it to {a}.\n");
        sb.Append("(Auto-detect which of the two it is.)\n");

        // Authenticity — a hard rule for every style.
        sb.Append(
            "Always preserve the author's original meaning and intent. Never add information, opinions, " +
            "or content that is not in the source.\n");

        string styleInstruction = StyleInstruction(style);
        if (styleInstruction.Length > 0)
        {
            sb.Append(styleInstruction);
            sb.Append('\n');
        }
        else
        {
            // Original: keep faithful and preserve incidental formatting/emojis the user typed.
            sb.Append("Preserve the tone, formatting, and any emojis of the original.\n");
        }

        if (humanize)
        {
            sb.Append(HumanizerLayer);
        }

        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>The per-style instruction appended to the base prompt (empty for <see cref="TranslationStyle.Original"/>).</summary>
    private static string StyleInstruction(TranslationStyle style) => style switch
    {
        TranslationStyle.Professional =>
            "Rewrite the message so it reads clearly, naturally, and professionally: correct any grammar " +
            "or word-choice mistakes, smooth out awkward or clumsy phrasing, and improve the flow. Keep " +
            "it polished but not stiff, and keep the output in the target language only.",
        TranslationStyle.Formal =>
            "Use a formal, respectful register in the target language: complete sentences, courteous and " +
            "professional phrasing, and no slang, casual contractions, or abbreviations.",
        TranslationStyle.Friendly =>
            "Use a warm, casual, conversational tone, as a friendly person would text, and add a few " +
            "relevant, tasteful emojis where they fit naturally (do not overdo it).",
        TranslationStyle.Email =>
            "Shape the output as a short, well-structured email in the target language: an appropriate " +
            "greeting on its own line, the message body, and a polite sign-off. Do not invent a specific " +
            "recipient or sender name.",
        TranslationStyle.Concise =>
            "Make the message noticeably shorter: keep only the essential point, cut greetings, " +
            "pleasantries, and any redundancy, and use as few words as possible while staying clear and polite.",
        TranslationStyle.Expand =>
            "Noticeably expand the message so it reads as a longer, more developed version: add natural " +
            "context, smoother transitions, and fuller, more complete sentences. Do not invent new facts, " +
            "requests, or information; only elaborate on what the user already means.",
        _ => string.Empty,
    };

    // Distilled from the humanizer skill ("Signs of AI writing"), scoped to short messaging text.
    // Scoped to QUALITY tells (how it reads), not length — length is owned by the Concise/Expand styles,
    // so the old "padding, filler" clause is dropped to avoid fighting Expand. The skill's "avoid emojis"
    // rule is also omitted (casual chat and the Friendly style use them).
    private const string HumanizerLayer =
        "Write the output the way a natural, fluent native speaker would, not like a machine " +
        "translation. Avoid these AI-writing tells: em dashes and en dashes (use commas, periods, or " +
        "parentheses); inflated, promotional, or hype wording; forced groups of three; needless " +
        "synonym-swapping; sycophantic or servile phrasing; and stiff, literal 'translationese'. Keep " +
        "the tone and register of the source.";

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
