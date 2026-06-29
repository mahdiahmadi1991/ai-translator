using System.Text.Json;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Models;

namespace AiTranslator.Core.Settings;

/// <summary>Persists settings as indented JSON. Returns defaults when the file is missing or corrupt.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    // camelCase property names match the documented schema (docs/reference/configuration.md) so a
    // hand-edited settings.json round-trips; case-insensitive read keeps any older PascalCase file
    // loadable. Dictionary KEYS (appOffsets exe names) are intentionally left untransformed.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
    private readonly string _filePath;

    public JsonSettingsStore(string filePath) => _filePath = filePath;

    /// <summary>The canonical per-user settings path: %APPDATA%\AI-Translator\settings.json.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AI-Translator", "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return AppSettings.Default;
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? AppSettings.Default;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, Options));
    }
}
