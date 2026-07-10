using AiTranslator.Core.Input;
using Xunit;

namespace AiTranslator.Tests;

public class HotkeyCombinationTests
{
    [Fact]
    public void Parses_modifiers_and_letter()
    {
        Assert.True(HotkeyCombination.TryParse("Ctrl+Alt+T", out var c));
        Assert.Equal(HotkeyCombination.ModControl | HotkeyCombination.ModAlt, c.Modifiers);
        Assert.Equal((uint)'T', c.VirtualKey);   // 0x54
    }

    [Fact]
    public void Is_case_insensitive_and_trims()
    {
        Assert.True(HotkeyCombination.TryParse("  ctrl + shift + k ", out var c));
        Assert.Equal(HotkeyCombination.ModControl | HotkeyCombination.ModShift, c.Modifiers);
        Assert.Equal((uint)'K', c.VirtualKey);
    }

    [Fact]
    public void Parses_function_key_without_modifiers()
    {
        Assert.True(HotkeyCombination.TryParse("F5", out var c));
        Assert.Equal(0u, c.Modifiers);
        Assert.Equal(0x74u, c.VirtualKey);
    }

    [Fact]
    public void Parses_digit_and_win_and_space()
    {
        Assert.True(HotkeyCombination.TryParse("Ctrl+1", out var c1));
        Assert.Equal((uint)'1', c1.VirtualKey);

        Assert.True(HotkeyCombination.TryParse("Win+Space", out var c2));
        Assert.Equal(HotkeyCombination.ModWin, c2.Modifiers);
        Assert.Equal(0x20u, c2.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Ctrl+")]          // no key
    [InlineData("Ctrl+Alt")]       // only modifiers, no key
    [InlineData("Ctrl+Foo")]       // unknown key
    [InlineData("Ctrl+F99")]       // out-of-range function key
    public void Rejects_invalid_input(string? text)
        => Assert.False(HotkeyCombination.TryParse(text, out _));
}
