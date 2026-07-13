using AiTranslator.Core.Models;

namespace AiTranslator.Core.Abstractions;

/// <summary>
/// Streams speech to text while the user is still speaking (ADR-0009). Implementations isolate all
/// provider-specific transport (the OpenAI Realtime WebSocket) so Core stays pure.
/// <para>
/// Lifecycle: <see cref="StartAsync"/> opens a session and begins listening; partial text arrives on
/// <see cref="PartialTranscript"/> as it is recognized, and <see cref="FinalTranscript"/> carries the
/// cleaner, punctuated transcript once the utterance is closed by <see cref="StopAsync"/>.
/// </para>
/// </summary>
public interface ISpeechRecognizer : IAsyncDisposable
{
    /// <summary>Incremental text recognized so far in the current utterance (may be re-sent in full).</summary>
    event EventHandler<string>? PartialTranscript;

    /// <summary>The finished transcript of an utterance; supersedes the partials that led to it.</summary>
    event EventHandler<string>? FinalTranscript;

    /// <summary>Listening lifecycle, for driving the mic button's state.</summary>
    event EventHandler<SpeechState>? StateChanged;

    /// <summary>A fault that ended the session (no key, no microphone, network, provider error).</summary>
    event EventHandler<Exception>? Failed;

    /// <summary>Open a session and start listening. Safe to call only when idle.</summary>
    Task StartAsync(SpeechOptions options, CancellationToken ct = default);

    /// <summary>Stop listening, flush the final transcript, and close the session. Idempotent.</summary>
    Task StopAsync(CancellationToken ct = default);
}
