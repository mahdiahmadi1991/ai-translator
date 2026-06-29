using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Settings;
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
        services.AddSingleton<ITextInjector, ClipboardTextInjector>();
        services.AddSingleton<ITranslationService>(sp =>
            new OpenAiTranslationService(() => sp.GetRequiredService<ISecretStore>().GetApiKey()));

        return services.BuildServiceProvider();
    }
}
