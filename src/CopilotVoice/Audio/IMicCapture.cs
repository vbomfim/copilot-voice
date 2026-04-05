namespace CopilotVoice.Audio;

/// <summary>
/// Captures microphone audio in PCM16 format (16 kHz, mono).
/// Start/Stop are idempotent — calling Start while already capturing is a no-op.
/// </summary>
public interface IMicCapture : IDisposable
{
    /// <summary>Begin capturing audio from the default microphone.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Stop capturing audio. Idempotent — safe to call when not capturing.</summary>
    Task StopAsync();

    /// <summary>Fired when a chunk of PCM16 audio is captured.</summary>
    event Action<ReadOnlyMemory<byte>>? AudioCaptured;

    /// <summary>Whether the mic is currently capturing audio.</summary>
    bool IsCapturing { get; }
}
