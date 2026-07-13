using AiTranslator.Core.Correction;
using Xunit;

namespace AiTranslator.Tests;

public class CorrectionPromptBuilderTests
{
    private static readonly string Prompt = CorrectionPromptBuilder.Build();

    [Fact]
    public void It_asks_for_spelling_and_typo_repair()
    {
        Assert.Contains("spelling", Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("typo", Prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void It_asks_to_repair_garbled_words_from_context()
    {
        Assert.Contains("garbled", Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("context", Prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void It_asks_to_restore_transliterated_english_terms()
        => Assert.Contains("transliteration", Prompt, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void It_forbids_translating()
        => Assert.Contains("NEVER translate", Prompt);

    [Fact]
    public void It_separates_spelling_from_style_so_it_cannot_fight_the_rewrite_styles()
    {
        // The rule that stops auto-correct from formalizing colloquial writing (ADR-0007 owns style).
        Assert.Contains("never the STYLE", Prompt);
        Assert.Contains("register", Prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void It_protects_code_urls_and_identifiers()
    {
        Assert.Contains("URLs", Prompt);
        Assert.Contains("identifiers", Prompt);
    }

    [Fact]
    public void It_requires_already_correct_text_to_pass_through_unchanged()
        => Assert.Contains("return it completely unchanged", Prompt);

    [Fact]
    public void It_forbids_commentary_so_the_output_is_the_text_itself()
        => Assert.Contains("no commentary", Prompt);

    [Fact]
    public void Prompt_text_contains_no_em_or_en_dashes()
    {
        Assert.DoesNotContain('—', Prompt);
        Assert.DoesNotContain('–', Prompt);
    }
}
