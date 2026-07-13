using AiTranslator.Core.Abstractions;
using NAudio.Wave;

namespace AiTranslator.Infrastructure.Speech;

/// <summary>
/// Captures the default microphone as 24 kHz / 16-bit / mono PCM, exactly the format the realtime
/// recognizer expects (ADR-0009), so no resampling is needed. NAudio's <see cref="WaveInEvent"/> is
/// used because it lets us ask the driver for that format directly and raises buffers on a worker
/// thread rather than a UI thread.
/// </summary>
public sealed class NAudioMicrophoneCapture : IAudioCapture
{
    private const int BufferMs = 100;   // matches the cadence the recognizer streams at

    private readonly object _gate = new();
    private WaveInEvent? _device;

    public event EventHandler<ReadOnlyMemory<byte>>? DataAvailable;
    public event EventHandler<Exception>? Failed;

    public void Start()
    {
        lock (_gate)
        {
            if (_device is not null)
            {
                return;
            }

            if (WaveInEvent.DeviceCount == 0)
            {
                throw new InvalidOperationException("No microphone was found.");
            }

            var device = new WaveInEvent
            {
                WaveFormat = new WaveFormat(IAudioCapture.SampleRate, bits: 16, channels: 1),
                BufferMilliseconds = BufferMs,
            };

            device.DataAvailable += OnData;
            device.RecordingStopped += OnStopped;

            // StartRecording throws when the device is busy or blocked by Windows privacy settings;
            // let it surface so the caller can show a real message instead of failing silently.
            device.StartRecording();
            _device = device;
        }
    }

    public void Stop()
    {
        WaveInEvent? device;
        lock (_gate)
        {
            device = _device;
            _device = null;
        }

        if (device is null)
        {
            return;
        }

        device.DataAvailable -= OnData;
        device.RecordingStopped -= OnStopped;
        try { device.StopRecording(); } catch { /* already stopped or device gone */ }
        device.Dispose();
    }

    public void Dispose() => Stop();

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded > 0)
        {
            DataAvailable?.Invoke(this, e.Buffer.AsMemory(0, e.BytesRecorded));
        }
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        // Only an unexpected stop is a fault; a requested Stop() unhooks this first.
        if (e.Exception is { } ex)
        {
            Failed?.Invoke(this, ex);
        }
    }
}
