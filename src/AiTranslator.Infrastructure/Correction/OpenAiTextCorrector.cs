// OPENAI001: the Responses API is shipped as an [Experimental] preview (see ADR-0002). The suppression
// is scoped to this file so the experimental surface stays isolated, exactly as it is for translation.
#pragma warning disable OPENAI001
using System.Text;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Correction;
using OpenAI.Responses;

namespace AiTranslator.Infrastructure.Correction;

/// <summary>
/// Runs the auto-correct pass through the OpenAI Responses API (ADR-0010). One short, non-streaming
/// call: the box needs the whole corrected text at once, not a live reveal. All OpenAI types stay in
/// this file, the same containment rule the translation client follows.
/// </summary>
public sealed class OpenAiTextCorrector : ITextCorrector
{
    private readonly Func<string?> _apiKeyProvider;

    public OpenAiTextCorrector(Func<string?> apiKeyProvider) => _apiKeyProvider = apiKeyProvider;

    public async Task<string> CorrectAsync(string text, string model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var apiKey = _apiKeyProvider()
            ?? throw new InvalidOperationException("No OpenAI API key configured.");

        var client = new ResponsesClient(apiKey: apiKey);
        var options = new CreateResponseOptions
        {
            Model = model,
            StreamingEnabled = true,
            Instructions = CorrectionPromptBuilder.Build(),
        };
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(text));

        // Streamed and reassembled rather than a single non-streaming call, so this reuses the exact
        // surface the translation client already relies on.
        var corrected = new StringBuilder();
        await foreach (var update in client.CreateResponseStreamingAsync(options, ct).ConfigureAwait(false))
        {
            if (update is StreamingResponseOutputTextDeltaUpdate delta && !string.IsNullOrEmpty(delta.Delta))
            {
                corrected.Append(delta.Delta);
            }
        }

        var result = corrected.ToString().Trim();

        // A model that returns nothing must never wipe the user's text.
        return result.Length == 0 ? text : result;
    }
}
