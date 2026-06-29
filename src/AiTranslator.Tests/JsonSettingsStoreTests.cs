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
