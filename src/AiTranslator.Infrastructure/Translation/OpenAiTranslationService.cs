// OPENAI001: the OpenAI Responses API (ResponsesClient, CreateResponseOptions, ResponseItem,
// StreamingResponseOutputTextDeltaUpdate) is shipped as an [Experimental] preview. ADR-0002 chose it
// deliberately; the suppression is scoped to this one file so the experimental surface stays isolated.
#pragma warning disable OPENAI001
using System.Runtime.CompilerServices;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Models;
using AiTranslator.Core.Translation;
using OpenAI.Responses;

namespace AiTranslator.Infrastructure.Translation;

/// <summary>
/// Streams a translation via the OpenAI Responses API. All OpenAI-specific types are isolated in
/// this file so SDK version drift touches nothing else (ADR-0002).
/// </summary>
/// <remarks>
/// API shape verified via context7 (/openai/openai-dotnet): <c>ResponsesClient</c> +
/// <c>CreateResponseOptions { StreamingEnabled = true }</c> + <c>CreateResponseStreamingAsync</c>
/// yielding <c>StreamingResponseUpdate</c>; text chunks are <c>StreamingResponseOutputTextDeltaUpdate.Delta</c>.
/// Confirm the exact member names (notably <c>Instructions</c> and the cancellation overload)
/// against the installed package when building on Windows.
/// </remarks>
public sealed class OpenAiTranslationService : ITranslationService
{
    private readonly Func<string?> _apiKeyProvider;

    public OpenAiTranslationService(Func<string?> apiKeyProvider) => _apiKeyProvider = apiKeyProvider;

    public async IAsyncEnumerable<string> TranslateStreamAsync(
        string text, TranslationDirection direction, string model,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var apiKey = _apiKeyProvider()
            ?? throw new InvalidOperationException("No OpenAI API key configured.");

        var pair = new LanguagePair(direction.SourceLang, direction.TargetLang);
        var systemPrompt = PromptBuilder.BuildSystemPrompt(pair);

        var client = new ResponsesClient(apiKey: apiKey);
        var options = new CreateResponseOptions
        {
            Model = model,
            StreamingEnabled = true,
            Instructions = systemPrompt,
        };
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(text));

        await foreach (var update in client.CreateResponseStreamingAsync(options, ct))
        {
            if (update is StreamingResponseOutputTextDeltaUpdate delta && !string.IsNullOrEmpty(delta.Delta))
            {
                yield return delta.Delta;
            }
        }
    }
}
