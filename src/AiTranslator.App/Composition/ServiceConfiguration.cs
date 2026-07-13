using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Settings;
using AiTranslator.Core.Translation;
using AiTranslator.Infrastructure.Awareness;
using AiTranslator.Infrastructure.Input;
using AiTranslator.Infrastructure.Secrets;
using AiTranslator.Infrastructure.Speech;
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

        // Dictation (ADR-0009): the recognizer drives the microphone, so the box only starts/stops it.
        services.AddSingleton<IAudioCapture, NAudioMicrophoneCapture>();
        services.AddSingleton<ISpeechRecognizer>(sp => new OpenAiRealtimeSpeechRecognizer(
            () => sp.GetRequiredService<ISecretStore>().GetApiKey(),
            sp.GetRequiredService<IAudioCapture>()));
        // Short-lived cache decorator: re-selecting the same text renders instantly and skips the API.
        // OpenAiTranslationService is left untouched — the cache wraps it transparently.
        services.AddSingleton<ITranslationService>(sp =>
            new CachingTranslationService(
                new OpenAiTranslationService(() => sp.GetRequiredService<ISecretStore>().GetApiKey())));

        return services.BuildServiceProvider();
    }
}
