using AiTranslator.Core.Awareness;
using Xunit;

namespace AiTranslator.Tests;

public class AppActivationPolicyTests
{
    private static readonly string[] NoBlock = [];
    private static readonly string[] Block = ["keepass", "1password"];

    [Fact]
    public void Activates_everywhere_when_blocklist_is_empty()
        => Assert.True(AppActivationPolicy.ShouldActivate("Telegram.exe", NoBlock));

    [Fact]
    public void Activates_for_any_app_not_on_the_blocklist()
        => Assert.True(AppActivationPolicy.ShouldActivate("Code.exe", Block));

    [Fact]
    public void Blocklisted_app_does_not_activate()
        => Assert.False(AppActivationPolicy.ShouldActivate("KeePass.exe", Block));

    [Fact]
    public void Blocklist_moniker_matches_case_insensitively_on_a_full_path()
        => Assert.False(AppActivationPolicy.ShouldActivate(@"C:\Tools\1Password\1Password.exe", ["1password"]));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_or_blank_foreground_does_not_activate(string? exe)
        => Assert.False(AppActivationPolicy.ShouldActivate(exe, NoBlock));
}
