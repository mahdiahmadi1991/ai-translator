using AiTranslator.Core.Models;
using AiTranslator.Core.Translation;
using Xunit;

namespace AiTranslator.Tests;

public class PromptBuilderTests
{
    [Fact]
    public void System_prompt_names_both_languages_and_forbids_chatter()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(new LanguagePair("fa", "en"));
        Assert.Contains("Persian", prompt);
        Assert.Contains("English", prompt);
        Assert.Contains("ONLY the translation", prompt);
    }

    [Fact]
    public void Unknown_code_falls_back_to_the_code_itself()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(new LanguagePair("fa", "xx"));
        Assert.Contains("xx", prompt);
    }

    [Fact]
    public void Every_style_preserves_meaning_and_intent()
    {
        foreach (TranslationStyle style in Enum.GetValues<TranslationStyle>())
        {
            var prompt = PromptBuilder.BuildSystemPrompt(new LanguagePair("fa", "en"), style, humanize: false);
            Assert.Contains("original meaning and intent", prompt);   // authenticity is a hard rule everywhere
        }
    }

    [Theory]
    [InlineData(TranslationStyle.Formal, "formal")]
    [InlineData(TranslationStyle.Friendly, "emoji")]
    [InlineData(TranslationStyle.Email, "email")]
    [InlineData(TranslationStyle.Concise, "shorter")]
    [InlineData(TranslationStyle.Expand, "expand")]
    [InlineData(TranslationStyle.Professional, "professionally")]
    public void Style_adds_its_instruction(TranslationStyle style, string marker)
    {
        var prompt = PromptBuilder.BuildSystemPrompt(new LanguagePair("fa", "en"), style, humanize: false);
        Assert.Contains(marker, prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Original_style_adds_no_rewrite_instruction()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(new LanguagePair("fa", "en"), TranslationStyle.Original, humanize: false);
        Assert.DoesNotContain("Rewrite the translation", prompt);
        Assert.Contains("Preserve the tone", prompt);
    }

    [Fact]
    public void Humanizer_layer_is_added_only_when_requested()
    {
        var with = PromptBuilder.BuildSystemPrompt(new LanguagePair("fa", "en"), TranslationStyle.Original, humanize: true);
        var without = PromptBuilder.BuildSystemPrompt(new LanguagePair("fa", "en"), TranslationStyle.Original, humanize: false);

        Assert.Contains("native speaker", with);
        Assert.Contains("em dashes", with);
        Assert.DoesNotContain("native speaker", without);
    }

    [Fact]
    public void Source_repair_layer_is_added_only_when_requested()
    {
        // Auto-correct rides along with the translation instead of costing a second round-trip (ADR-0010).
        var with = PromptBuilder.BuildSystemPrompt(
            new LanguagePair("fa", "en"), TranslationStyle.Original, humanize: false, correctSource: true);
        var without = PromptBuilder.BuildSystemPrompt(
            new LanguagePair("fa", "en"), TranslationStyle.Original, humanize: false, correctSource: false);

        Assert.Contains("typos", with);
        Assert.Contains("translate what the user MEANT", with);
        Assert.DoesNotContain("typos", without);
    }

    [Fact]
    public void Source_repair_never_licenses_changing_the_message()
    {
        // Reading through a typo must not become permission to rewrite: authenticity still binds.
        var prompt = PromptBuilder.BuildSystemPrompt(
            new LanguagePair("fa", "en"), TranslationStyle.Original, humanize: true, correctSource: true);

        Assert.Contains("original meaning and intent", prompt);
        Assert.Contains("Never add information", prompt);
    }

    [Fact]
    public void Prompt_text_contains_no_em_or_en_dashes()
    {
        // The prompt tells the model to avoid them; keep the instruction itself clean too.
        foreach (TranslationStyle style in Enum.GetValues<TranslationStyle>())
        {
            foreach (bool correctSource in new[] { false, true })
            {
                var prompt = PromptBuilder.BuildSystemPrompt(
                    new LanguagePair("fa", "en"), style, humanize: true, correctSource);
                Assert.DoesNotContain('—', prompt);   // em dash
                Assert.DoesNotContain('–', prompt);   // en dash
            }
        }
    }
}
