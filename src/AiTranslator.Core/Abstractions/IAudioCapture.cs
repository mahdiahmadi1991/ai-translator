namespace AiTranslator.Core.Abstractions;

/// <summary>
/// Captures microphone audio as the raw PCM the recognizer expects: 24 kHz, 16-bit, mono
/// (see ADR-0009). Implementations wrap the platform audio stack; this seam keeps it out of Core.
/// </summary>
public interface IAudioCapture : IDisposable
{
    /// <summary>Sample rate the recognizer requires.</summary>
    public const int SampleRate = 24_000;

    /// <summary>Raised for each captured buffer of little-endian 16-bit mono PCM.</summary>
    event EventHandler<ReadOnlyMemory<byte>>? DataAvailable;

    /// <summary>Raised when capture stops on its own (device removed, driver error).</summary>
    event EventHandler<Exception>? Failed;

    /// <summary>Begin capturing. Throws when no microphone is available or access is denied.</summary>
    void Start();

    /// <summary>Stop capturing. Idempotent.</summary>
    void Stop();
}
