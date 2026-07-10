namespace AiTranslator.Core.Abstractions;

/// <summary>Stores the OpenAI API key in OS-secure storage (never in the repo or settings file).</summary>
public interface ISecretStore
{
    string? GetApiKey();
    void SetApiKey(string apiKey);
    void DeleteApiKey();
}
