using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Settings;
using AiTranslator.Core.Translation;
using AiTranslator.Infrastructure.Awareness;
using AiTranslator.Infrastructure.Input;
using AiTranslator.Infrastructure.Secrets;
using AiTranslator.Infrastructure.Translation;
using Microsoft.Extensions.DependencyInjection;

namespace AiTranslator.App.Composition;

/// <summary>Builds the DI container. Hotkey + windows are created in <see cref="App"/> (need the HWND).</summary>
public static class ServiceConfiguration
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(JsonSettingsStore.DefaultPath));
        services.AddSingleton<ISecretStore, CredentialManagerSecretStore>();
        services.AddSingleton<IFocusTargetProvider, ForegroundFocusTargetProvider>();
        services.AddSingleton<ITargetResolver, TargetResolver>();
        services.AddSingleton<ITextInjector, ClipboardTextInjector>();
        // Short-lived cache decorator: re-selecting the same text renders instantly and skips the API.
        // OpenAiTranslationService is left untouched — the cache wraps it transparently.
        services.AddSingleton<ITranslationService>(sp =>
            new CachingTranslationService(
                new OpenAiTranslationService(() => sp.GetRequiredService<ISecretStore>().GetApiKey())));

        return services.BuildServiceProvider();
    }
}
