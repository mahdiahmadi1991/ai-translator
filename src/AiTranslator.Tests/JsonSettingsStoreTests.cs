using AiTranslator.Core.Awareness;
using AiTranslator.Core.Models;
using AiTranslator.Core.Settings;
using Xunit;

namespace AiTranslator.Tests;

public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"ait-{Guid.NewGuid():N}", "settings.json");

    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        var store = new JsonSettingsStore(_path);
        Assert.Equal(AppSettings.Default, store.Load());
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var store = new JsonSettingsStore(_path);
        var custom = AppSettings.Default with { Model = "gpt-5.1-mini", DebounceMs = 750 };
        store.Save(custom);

        var reloaded = new JsonSettingsStore(_path).Load();
        Assert.Equal("gpt-5.1-mini", reloaded.Model);
        Assert.Equal(750, reloaded.DebounceMs);
        Assert.Equal("fa", reloaded.LanguagePair.Primary);
    }

    [Fact]
    public void Save_then_load_round_trips_app_offsets_and_lists()
    {
        var store = new JsonSettingsStore(_path);
        var custom = AppSettings.Default with
        {
            AutoAppearBadge = false,
            Blocklist = new[] { "KeePass.exe", "1password" },
            AppOffsets = new Dictionary<string, AppOffset>
            {
                ["WhatsApp.exe"] = new AppOffset(Corner: 2, Dx: 12, Dy: -4),
                ["Telegram.exe"] = new AppOffset(Corner: 1, Dx: 64, Dy: -6),
            },
        };
        store.Save(custom);

        var reloaded = new JsonSettingsStore(_path).Load();
        Assert.False(reloaded.AutoAppearBadge);
        Assert.Equal(new[] { "KeePass.exe", "1password" }, reloaded.Blocklist);
        Assert.Equal(2, reloaded.AppOffsets.Count);
        Assert.Equal(new AppOffset(2, 12, -4), reloaded.AppOffsets["WhatsApp.exe"]);
        Assert.Equal(new AppOffset(1, 64, -6), reloaded.AppOffsets["Telegram.exe"]);
    }

    [Fact]
    public void Saves_camel_case_keys_matching_the_documented_schema()
    {
        new JsonSettingsStore(_path).Save(AppSettings.Default with { AutoAppearBadge = false });
        var json = File.ReadAllText(_path);

        Assert.Contains("\"autoAppearBadge\"", json);   // docs/reference/configuration.md uses camelCase
        Assert.Contains("\"blocklist\"", json);
        Assert.DoesNotContain("\"AutoAppearBadge\"", json);
    }

    [Fact]
    public void Loads_legacy_pascal_case_file()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ \"Model\": \"legacy-x\", \"DebounceMs\": 999 }");

        var loaded = new JsonSettingsStore(_path).Load();
        Assert.Equal("legacy-x", loaded.Model);   // case-insensitive read keeps old PascalCase files working
        Assert.Equal(999, loaded.DebounceMs);
    }

    [Fact]
    public void Load_returns_defaults_on_corrupt_json()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ not valid json");
        Assert.Equal(AppSettings.Default, new JsonSettingsStore(_path).Load());
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
