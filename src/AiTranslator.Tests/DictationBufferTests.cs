using AiTranslator.Core.Speech;
using Xunit;

namespace AiTranslator.Tests;

public class DictationBufferTests
{
    private static DictationBuffer Started(string? existing = "")
    {
        var buffer = new DictationBuffer();
        buffer.Begin(existing);
        return buffer;
    }

    [Fact]
    public void Empty_box_shows_the_partial_as_is()
    {
        var b = Started();
        Assert.Equal("سلام", b.ApplyPartial("سلام"));
    }

    [Fact]
    public void Dictation_is_appended_after_what_the_user_typed()
    {
        var b = Started("Hello");
        Assert.Equal("Hello world", b.ApplyPartial("world"));
    }

    [Fact]
    public void Existing_trailing_whitespace_is_not_doubled()
    {
        var b = Started("Hello ");
        Assert.Equal("Hello world", b.ApplyPartial("world"));
    }

    [Fact]
    public void A_partial_replaces_the_previous_partial_rather_than_appending()
    {
        var b = Started();
        b.ApplyPartial("سلام");
        b.ApplyPartial("سلام لطفا");
        Assert.Equal("سلام لطفا گزارش", b.ApplyPartial("سلام لطفا گزارش"));
    }

    [Fact]
    public void A_revised_partial_never_duplicates_text()
    {
        var b = Started("Note:");
        b.ApplyPartial("teh quick");
        Assert.Equal("Note: the quick", b.ApplyPartial("the quick"));   // model corrected itself
    }

    [Fact]
    public void Final_supersedes_the_partial_that_led_to_it()
    {
        var b = Started();
        b.ApplyPartial("سلام لطفا گزارش");
        // the final transcript is the cleaner, punctuated one
        Assert.Equal("سلام، لطفاً گزارش را بفرست.", b.ApplyFinal("سلام، لطفاً گزارش را بفرست."));
    }

    [Fact]
    public void A_second_utterance_is_appended_after_the_first_final()
    {
        var b = Started();
        b.ApplyFinal("سلام.");
        Assert.Equal("سلام. حالت چطور است؟", b.ApplyPartial("حالت چطور است؟"));
    }

    [Fact]
    public void Two_finals_are_separated()
    {
        var b = Started();
        b.ApplyFinal("سلام.");
        Assert.Equal("سلام. ممنون.", b.ApplyFinal("ممنون."));
    }

    [Fact]
    public void Flush_keeps_a_partial_that_never_got_a_final()
    {
        var b = Started("Draft:");
        b.ApplyPartial("half a sentence");
        Assert.Equal("Draft: half a sentence", b.Flush());
    }

    [Fact]
    public void Flush_is_idempotent()
    {
        var b = Started();
        b.ApplyPartial("hello");
        b.Flush();
        Assert.Equal("hello", b.Flush());
    }

    [Fact]
    public void Blank_partials_and_finals_are_ignored()
    {
        var b = Started("Hello");
        Assert.Equal("Hello", b.ApplyPartial("   "));
        Assert.Equal("Hello", b.ApplyFinal(null));
        Assert.Equal("Hello", b.Text);
    }

    [Fact]
    public void Leading_delta_whitespace_is_trimmed()
    {
        // Realtime deltas arrive as " سلام", " لطفا" — the accumulation keeps the leading space.
        var b = Started();
        Assert.Equal("سلام لطفا", b.ApplyPartial(" سلام لطفا"));
    }

    [Fact]
    public void Begin_resets_a_previous_session()
    {
        var b = Started("first");
        b.ApplyFinal("dictated");
        b.Begin("second");
        Assert.Equal("second", b.Text);
    }
}
