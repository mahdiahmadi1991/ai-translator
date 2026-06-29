using AiTranslator.Core.Abstractions;
using Meziantou.Framework.Win32;

namespace AiTranslator.Infrastructure.Secrets;

/// <summary>
/// Stores the OpenAI key in Windows Credential Manager (Generic credential), DPAPI-backed per user
/// (ADR-0005). Verify the Meziantou API names (<c>ReadCredential</c>/<c>WriteCredential</c>/
/// <c>DeleteCredential</c>, <c>CredentialPersistence</c>, <c>.Password</c>) when building on Windows.
/// </summary>
public sealed class CredentialManagerSecretStore : ISecretStore
{
    private const string Target = "AI-Translator:OpenAI";
    private const string User = "openai";

    public string? GetApiKey()
        => CredentialManager.ReadCredential(Target)?.Password;

    public void SetApiKey(string apiKey)
        => CredentialManager.WriteCredential(Target, User, apiKey, CredentialPersistence.LocalMachine);

    public void DeleteApiKey()
        => CredentialManager.DeleteCredential(Target);
}
