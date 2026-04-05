namespace CopilotVoice.Audio;

/// <summary>
/// Stub audio playback implementation.
/// Satisfies the IAudioPlayer interface but does not play real audio.
/// TODO(#63): Replace with platform audio implementation
/// (CoreAudio on macOS, WASAPI on Windows, ALSA on Linux, or cross-platform PortAudio).
/// </summary>
public sealed class AudioPlayer : IAudioPlayer
{
    private bool _disposed;

    public bool IsPlaying { get; private set; }

    public event Action? PlaybackCompleted;

    public Task PlayAsync(ReadOnlyMemory<byte> pcm16Audio, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IsPlaying = true;
        Console.Error.WriteLine($"[AudioPlayer] PlayAsync called with {pcm16Audio.Length} bytes (stub — no real audio played)");

        // Stub: immediately complete playback
        IsPlaying = false;
        PlaybackCompleted?.Invoke();

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsPlaying = false;
        Console.Error.WriteLine("[AudioPlayer] Stopped (stub)");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsPlaying = false;
    }
}
