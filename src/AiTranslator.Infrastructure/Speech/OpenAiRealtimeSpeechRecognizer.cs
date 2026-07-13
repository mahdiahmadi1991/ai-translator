using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Models;

namespace AiTranslator.Infrastructure.Speech;

/// <summary>
/// Dictation over the OpenAI Realtime transcription session (ADR-0009). Every provider and transport
/// detail lives here — the WebSocket, the event names, the JSON — so the rest of the app only sees
/// <see cref="ISpeechRecognizer"/>, the same containment rule the translation client follows.
/// <para>
/// Verified against the live API: transcript deltas stream <b>while</b> audio is being appended, so the
/// buffer is committed only on stop, and the resulting <c>completed</c> event carries a cleaner,
/// punctuated transcript that supersedes the deltas.
/// </para>
/// </summary>
public sealed class OpenAiRealtimeSpeechRecognizer : ISpeechRecognizer
{
    private const string Endpoint = "wss://api.openai.com/v1/realtime?intent=transcription";
    private const string DeltaEvent = "conversation.item.input_audio_transcription.delta";
    private const string CompletedEvent = "conversation.item.input_audio_transcription.completed";

    /// <summary>How long to wait, after committing, for the model to flush the final transcript.</summary>
    private static readonly TimeSpan FinalTranscriptTimeout = TimeSpan.FromSeconds(8);

    private readonly Func<string?> _apiKeyProvider;
    private readonly IAudioCapture _capture;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _session;
    private Channel<byte[]>? _audio;
    private Task? _pump;
    private Task? _receive;
    private TaskCompletionSource? _finalArrived;
    private readonly StringBuilder _partial = new();
    private SpeechState _state = SpeechState.Idle;
    private int _faulted;   // 0/1 — one Failed per session, however many layers notice the same fault

    public OpenAiRealtimeSpeechRecognizer(Func<string?> apiKeyProvider, IAudioCapture capture)
    {
        _apiKeyProvider = apiKeyProvider;
        _capture = capture;
    }

    public event EventHandler<string>? PartialTranscript;
    public event EventHandler<string>? FinalTranscript;
    public event EventHandler<SpeechState>? StateChanged;
    public event EventHandler<Exception>? Failed;

    /// <summary>Open the session and start listening. Throws if there is no key or no usable microphone
    /// (the caller shows that as a message); faults *after* listening begins arrive on <see cref="Failed"/>.</summary>
    public async Task StartAsync(SpeechOptions options, CancellationToken ct = default)
    {
        if (_state != SpeechState.Idle)
        {
            return;
        }

        _faulted = 0;
        SetState(SpeechState.Connecting);
        try
        {
            var apiKey = _apiKeyProvider()
                ?? throw new InvalidOperationException("No OpenAI API key configured.");

            _session = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _session.Token;

            var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            await socket.ConnectAsync(new Uri(Endpoint), token).ConfigureAwait(false);
            _socket = socket;

            await SendAsync(BuildSessionUpdate(options), token).ConfigureAwait(false);

            _partial.Clear();
            _finalArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _receive = Task.Run(() => ReceiveLoopAsync(socket, token), CancellationToken.None);

            // A channel keeps audio ordered and never blocks the capture thread on a network send.
            _audio = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
            _pump = Task.Run(() => PumpAudioAsync(_audio.Reader, token), CancellationToken.None);

            _capture.DataAvailable += OnAudioCaptured;
            _capture.Failed += OnCaptureFailed;
            _capture.Start();   // throws when the mic is missing or blocked by Windows privacy settings

            SetState(SpeechState.Listening);
        }
        catch
        {
            await CleanUpAsync().ConfigureAwait(false);
            SetState(SpeechState.Idle);
            throw;
        }
    }

    /// <summary>Stop listening, commit the utterance, and wait briefly for the final transcript.</summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_state is SpeechState.Idle or SpeechState.Stopping)
        {
            return;
        }

        SetState(SpeechState.Stopping);
        try
        {
            _capture.DataAvailable -= OnAudioCaptured;
            _capture.Failed -= OnCaptureFailed;
            _capture.Stop();

            // Drain the audio already captured before telling the model the utterance is over.
            _audio?.Writer.TryComplete();
            if (_pump is { } pump)
            {
                await pump.WaitAsync(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            }

            if (_socket is { State: WebSocketState.Open })
            {
                await SendAsync("""{"type":"input_audio_buffer.commit"}""", ct).ConfigureAwait(false);
                if (_finalArrived is { } final)
                {
                    // Best effort: if the final never lands, the caller keeps the partial it already has.
                    await Task.WhenAny(final.Task, Task.Delay(FinalTranscriptTimeout, ct)).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Failed?.Invoke(this, ex);
        }
        finally
        {
            await CleanUpAsync().ConfigureAwait(false);
            SetState(SpeechState.Idle);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _sendLock.Dispose();
    }

    // ---- transport ----------------------------------------------------------------------------

    private static string BuildSessionUpdate(SpeechOptions options) => JsonSerializer.Serialize(new
    {
        type = "session.update",
        session = new
        {
            type = "transcription",
            audio = new
            {
                input = new
                {
                    format = new { type = "audio/pcm", rate = IAudioCapture.SampleRate },
                    transcription = new
                    {
                        model = options.Model,
                        language = options.LanguageCode,
                        delay = "medium",   // the latency/accuracy trade-off; medium reads well live
                    },
                    // gpt-realtime-whisper does not use server VAD: we commit the buffer ourselves on stop.
                    turn_detection = (object?)null,
                },
            },
        },
    });

    private void OnAudioCaptured(object? sender, ReadOnlyMemory<byte> buffer)
        => _audio?.Writer.TryWrite(buffer.ToArray());

    private void OnCaptureFailed(object? sender, Exception ex) => Fault(ex);

    /// <summary>
    /// A session died on us. Report it <b>once</b> and always wind the session back to
    /// <see cref="SpeechState.Idle"/>.
    /// <para>
    /// This is the rule the whole class hangs on: the UI derives "the box is locked, the microphone is
    /// live, Translate is disabled" purely from the speech state, so a fault that only logs and leaves
    /// the state at <see cref="SpeechState.Listening"/> freezes the compose box forever, with the mic
    /// still open and no error shown. A dropped socket must therefore end the session, not just be
    /// noticed.
    /// </para>
    /// </summary>
    private void Fault(Exception ex)
    {
        if (_state is SpeechState.Idle or SpeechState.Stopping)
        {
            return;   // already going down (usually our own teardown closing the socket) — not a fault
        }

        if (Interlocked.Exchange(ref _faulted, 1) != 0)
        {
            return;   // another layer already reported this same death
        }

        Failed?.Invoke(this, ex);
        _ = StopAsync();
    }

    private async Task PumpAudioAsync(ChannelReader<byte[]> reader, CancellationToken ct)
    {
        try
        {
            await foreach (var buffer in reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var json = JsonSerializer.Serialize(new
                {
                    type = "input_audio_buffer.append",
                    audio = Convert.ToBase64String(buffer),
                });
                await SendAsync(json, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // stopping
        }
        catch (Exception ex)
        {
            Fault(ex);
        }
    }

    private async Task SendAsync(string json, CancellationToken ct)
    {
        var socket = _socket;
        if (socket is not { State: WebSocketState.Open })
        {
            return;
        }

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[32 * 1024];
        var message = new StringBuilder();

        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // Our own teardown closes the socket too; Fault ignores that (we are Stopping by
                    // then). This branch is the service hanging up on a live session.
                    Fault(new InvalidOperationException("The speech service closed the session."));
                    return;
                }

                message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage)
                {
                    continue;
                }

                var json = message.ToString();
                message.Clear();
                Handle(json);
            }
        }
        catch (OperationCanceledException)
        {
            // stopping
        }
        catch (WebSocketException ex)
        {
            // The connection died mid-session (Wi-Fi blip, service hang-up). Swallowing this used to
            // leave the state at Listening for good: the box stayed read-only and the mic stayed open.
            Fault(new InvalidOperationException("The connection to the speech service dropped.", ex));
        }
        catch (Exception ex)
        {
            Fault(ex);
        }
    }

    private void Handle(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("type", out var typeElement))
        {
            return;
        }

        switch (typeElement.GetString())
        {
            case DeltaEvent:
                if (doc.RootElement.TryGetProperty("delta", out var delta))
                {
                    _partial.Append(delta.GetString());
                    PartialTranscript?.Invoke(this, _partial.ToString());
                }

                break;

            case CompletedEvent:
                if (doc.RootElement.TryGetProperty("transcript", out var transcript))
                {
                    _partial.Clear();
                    FinalTranscript?.Invoke(this, transcript.GetString() ?? string.Empty);
                }

                _finalArrived?.TrySetResult();
                break;

            case "error":
                Fault(new InvalidOperationException(ErrorMessage(doc.RootElement)));
                break;
        }
    }

    private static string ErrorMessage(JsonElement root)
        => root.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message)
            ? message.GetString() ?? "Speech recognition failed."
            : "Speech recognition failed.";

    private async Task CleanUpAsync()
    {
        _capture.DataAvailable -= OnAudioCaptured;
        _capture.Failed -= OnCaptureFailed;
        try { _capture.Stop(); } catch { /* already stopped */ }

        _audio?.Writer.TryComplete();
        _audio = null;

        _session?.Cancel();

        if (_socket is { } socket)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch { /* the socket is going away either way */ }

            socket.Dispose();
            _socket = null;
        }

        _session?.Dispose();
        _session = null;
        _pump = null;
        _receive = null;
        _finalArrived = null;
        _partial.Clear();
    }

    private void SetState(SpeechState state)
    {
        if (_state != state)
        {
            _state = state;
            StateChanged?.Invoke(this, state);
        }
    }
}
