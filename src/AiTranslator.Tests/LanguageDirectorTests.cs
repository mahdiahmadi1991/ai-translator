using AiTranslator.Core.Models;
using AiTranslator.Core.Translation;
using Xunit;

namespace AiTranslator.Tests;

public class LanguageDirectorTests
{
    private static readonly LanguagePair FaEn = new("fa", "en");

    [Fact]
    public void Persian_input_translates_to_english()
    {
        var d = LanguageDirector.Resolve("سلام خوبی؟", FaEn, autoDirection: true);
        Assert.Equal("fa", d.SourceLang);
        Assert.Equal("en", d.TargetLang);
    }

    [Fact]
    public void English_input_translates_to_persian()
    {
        var d = LanguageDirector.Resolve("Hello there", FaEn, autoDirection: true);
        Assert.Equal("en", d.SourceLang);
        Assert.Equal("fa", d.TargetLang);
    }

    [Fact]
    public void Auto_direction_off_always_primary_to_secondary()
    {
        var d = LanguageDirector.Resolve("Hello", FaEn, autoDirection: false);
        Assert.Equal("fa", d.SourceLang);
        Assert.Equal("en", d.TargetLang);
    }

    [Fact]
    public void Mixed_text_uses_dominant_script()
    {
        var d = LanguageDirector.Resolve("سلام world این متن فارسی است", FaEn, autoDirection: true);
        Assert.Equal("fa", d.SourceLang);
    }
}
