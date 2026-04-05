namespace CopilotVoice.Audio;

/// <summary>
/// Plays PCM16 audio data through the default audio output.
/// Supports stopping playback mid-stream and notifies on completion.
/// </summary>
public interface IAudioPlayer : IDisposable
{
    /// <summary>Play a buffer of PCM16 audio (16 kHz, mono).</summary>
    Task PlayAsync(ReadOnlyMemory<byte> pcm16Audio, CancellationToken ct = default);

    /// <summary>Stop any active playback immediately.</summary>
    Task StopAsync();

    /// <summary>Whether audio is currently playing.</summary>
    bool IsPlaying { get; }

    /// <summary>Fired when playback of a buffer completes (not fired on stop/cancel).</summary>
    event Action? PlaybackCompleted;
}
