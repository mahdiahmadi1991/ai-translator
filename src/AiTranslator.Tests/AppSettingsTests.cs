using AiTranslator.Core.Models;
using Xunit;

namespace AiTranslator.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Default_has_fa_en_pair_and_safe_defaults()
    {
        var s = AppSettings.Default;
        Assert.Equal(1, s.SchemaVersion);
        Assert.Equal("fa", s.LanguagePair.Primary);
        Assert.Equal("en", s.LanguagePair.Secondary);
        Assert.True(s.AutoDirection);
        Assert.Equal(500, s.DebounceMs);
        Assert.Equal("Ctrl+Alt+T", s.Hotkey);
        Assert.Equal("gpt-5.1", s.Model);
        Assert.False(s.AutoSend);
        Assert.Contains("whatsapp", s.Allowlist);   // regex moniker (matches WhatsApp.exe / WhatsApp.Root.exe)
    }
}
