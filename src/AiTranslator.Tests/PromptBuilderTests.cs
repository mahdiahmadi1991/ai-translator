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
}
