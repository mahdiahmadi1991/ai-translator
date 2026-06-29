using AiTranslator.Core.Awareness;
using Xunit;

namespace AiTranslator.Tests;

public class AppActivationPolicyTests
{
    private static readonly string[] Allow = ["WhatsApp.exe", "Telegram.exe"];
    private static readonly string[] NoBlock = [];

    [Fact]
    public void Activates_for_allowlisted_exe()
        => Assert.True(AppActivationPolicy.ShouldActivate("WhatsApp.exe", Allow, NoBlock));

    [Fact]
    public void Match_is_case_insensitive()
        => Assert.True(AppActivationPolicy.ShouldActivate("whatsapp.EXE", Allow, NoBlock));

    [Fact]
    public void Matches_on_filename_when_given_a_full_path()
        => Assert.True(AppActivationPolicy.ShouldActivate(@"C:\Program Files\WhatsApp\WhatsApp.exe", Allow, NoBlock));

    [Fact]
    public void Allowlist_entry_without_extension_still_matches()
        => Assert.True(AppActivationPolicy.ShouldActivate("WhatsApp.exe", ["whatsapp"], NoBlock));

    [Fact]
    public void Blocklist_takes_precedence_over_allowlist()
        => Assert.False(AppActivationPolicy.ShouldActivate("WhatsApp.exe", Allow, ["whatsapp.exe"]));

    [Fact]
    public void Does_not_activate_for_unlisted_exe()
        => Assert.False(AppActivationPolicy.ShouldActivate("notepad.exe", Allow, NoBlock));

    [Fact]
    public void Empty_allowlist_never_auto_activates()
        => Assert.False(AppActivationPolicy.ShouldActivate("WhatsApp.exe", [], NoBlock));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_or_blank_foreground_does_not_activate(string? exe)
        => Assert.False(AppActivationPolicy.ShouldActivate(exe, Allow, NoBlock));
}
