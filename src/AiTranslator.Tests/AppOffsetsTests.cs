using AiTranslator.Core.Awareness;
using AiTranslator.Core.Models;
using Xunit;

namespace AiTranslator.Tests;

public class AppOffsetsTests
{
    private static AppSettings WithOffsets(params (string Key, AppOffset Offset)[] entries)
        => AppSettings.Default with
        {
            AppOffsets = entries.ToDictionary(e => e.Key, e => e.Offset),
        };

    [Fact]
    public void Returns_default_when_no_offsets_configured()
        => Assert.Equal(AppOffset.Default, AppOffsets.For("WhatsApp.exe", AppSettings.Default));

    [Fact]
    public void Returns_calibrated_offset_for_matching_exe()
    {
        var custom = new AppOffset(Corner: 2, Dx: 10, Dy: 20);
        var settings = WithOffsets(("WhatsApp.exe", custom));
        Assert.Equal(custom, AppOffsets.For("WhatsApp.exe", settings));
    }

    [Fact]
    public void Match_is_case_insensitive()
    {
        var custom = new AppOffset(3, 1, 2);
        var settings = WithOffsets(("WhatsApp.exe", custom));
        Assert.Equal(custom, AppOffsets.For("whatsapp.EXE", settings));
    }

    [Fact]
    public void Key_without_extension_matches_a_full_path_foreground()
    {
        var custom = new AppOffset(0, 5, 5);
        var settings = WithOffsets(("whatsapp", custom));
        Assert.Equal(custom, AppOffsets.For(@"C:\Program Files\WhatsApp\WhatsApp.exe", settings));
    }

    [Fact]
    public void Returns_default_for_unconfigured_exe()
    {
        var settings = WithOffsets(("Telegram.exe", new AppOffset(2, 9, 9)));
        Assert.Equal(AppOffset.Default, AppOffsets.For("WhatsApp.exe", settings));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Returns_default_for_null_or_blank_exe(string? exe)
    {
        var settings = WithOffsets(("WhatsApp.exe", new AppOffset(2, 9, 9)));
        Assert.Equal(AppOffset.Default, AppOffsets.For(exe, settings));
    }

    [Fact]
    public void Default_offset_has_zero_nudge()
        => Assert.Equal(new AppOffset(Corner: 1, Dx: 0, Dy: 0), AppOffset.Default);
}
