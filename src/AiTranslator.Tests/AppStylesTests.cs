using AiTranslator.Core.Awareness;
using AiTranslator.Core.Models;
using Xunit;

namespace AiTranslator.Tests;

public class AppStylesTests
{
    private static AppSettings With(params (string Key, TranslationStyle Style)[] entries)
        => AppSettings.Default with { AppStyles = entries.ToDictionary(e => e.Key, e => e.Style) };

    // ---- lookup ------------------------------------------------------------------------------

    [Fact]
    public void Falls_back_to_the_global_default_when_the_app_has_no_memory()
    {
        var settings = AppSettings.Default with { RewriteStyle = TranslationStyle.Concise };
        Assert.Equal(TranslationStyle.Concise, AppStyles.For("Teams.exe", settings));
    }

    [Fact]
    public void Returns_the_style_remembered_for_the_app()
    {
        var settings = With(("ms-teams.exe", TranslationStyle.Formal));
        Assert.Equal(TranslationStyle.Formal, AppStyles.For("ms-teams.exe", settings));
    }

    [Fact]
    public void Each_app_keeps_an_independent_style()
    {
        var settings = With(
            ("ms-teams.exe", TranslationStyle.Formal),
            ("chrome.exe", TranslationStyle.Friendly));

        Assert.Equal(TranslationStyle.Formal, AppStyles.For("ms-teams.exe", settings));
        Assert.Equal(TranslationStyle.Friendly, AppStyles.For("chrome.exe", settings));
        Assert.Equal(TranslationStyle.Original, AppStyles.For("notepad.exe", settings));   // unlisted -> default
    }

    [Fact]
    public void Match_is_case_insensitive_and_works_against_a_full_path()
    {
        var settings = With(("teams", TranslationStyle.Email));
        Assert.Equal(TranslationStyle.Email, AppStyles.For(@"C:\Program Files\Teams\ms-teams.EXE", settings));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Unknown_app_falls_back_to_the_global_default(string? exe)
    {
        var settings = With(("ms-teams.exe", TranslationStyle.Formal)) with { RewriteStyle = TranslationStyle.Expand };
        Assert.Equal(TranslationStyle.Expand, AppStyles.For(exe, settings));
    }

    // ---- remember ----------------------------------------------------------------------------

    [Fact]
    public void Remember_stores_the_style_under_the_normalized_exe_name()
    {
        var updated = AppStyles.Remember(@"C:\Program Files\Teams\ms-teams.exe", TranslationStyle.Formal, AppSettings.Default);

        Assert.True(updated.AppStyles.ContainsKey("ms-teams.exe"));
        Assert.Equal(TranslationStyle.Formal, AppStyles.For("ms-teams.exe", updated));
        Assert.Equal(TranslationStyle.Original, updated.RewriteStyle);   // the global default is untouched
    }

    [Fact]
    public void Remember_does_not_leak_into_other_apps()
    {
        var settings = AppStyles.Remember("ms-teams.exe", TranslationStyle.Formal, AppSettings.Default);
        settings = AppStyles.Remember("chrome.exe", TranslationStyle.Friendly, settings);

        Assert.Equal(TranslationStyle.Formal, AppStyles.For("ms-teams.exe", settings));
        Assert.Equal(TranslationStyle.Friendly, AppStyles.For("chrome.exe", settings));
    }

    [Fact]
    public void Remember_replaces_the_apps_previous_style()
    {
        var settings = AppStyles.Remember("ms-teams.exe", TranslationStyle.Formal, AppSettings.Default);
        settings = AppStyles.Remember("ms-teams.exe", TranslationStyle.Concise, settings);

        Assert.Single(settings.AppStyles);
        Assert.Equal(TranslationStyle.Concise, AppStyles.For("ms-teams.exe", settings));
    }

    [Fact]
    public void Remember_collapses_a_shadowing_moniker_into_one_entry()
    {
        // A hand-written moniker ("teams") already matches; remembering must not leave two entries
        // where the older one could shadow the new choice.
        var settings = With(("teams", TranslationStyle.Formal));
        settings = AppStyles.Remember("ms-teams.exe", TranslationStyle.Friendly, settings);

        Assert.Single(settings.AppStyles);
        Assert.Equal(TranslationStyle.Friendly, AppStyles.For("ms-teams.exe", settings));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Remember_for_an_unknown_app_updates_the_global_default(string? exe)
    {
        var updated = AppStyles.Remember(exe, TranslationStyle.Friendly, AppSettings.Default);

        Assert.Equal(TranslationStyle.Friendly, updated.RewriteStyle);
        Assert.Empty(updated.AppStyles);
    }
}
