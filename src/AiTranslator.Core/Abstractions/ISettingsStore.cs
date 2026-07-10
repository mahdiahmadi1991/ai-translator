using AiTranslator.Core.Models;

namespace AiTranslator.Core.Abstractions;

/// <summary>Loads and persists non-secret application settings.</summary>
public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
