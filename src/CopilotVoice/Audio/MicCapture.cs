namespace CopilotVoice.Audio;

/// <summary>
/// Stub microphone capture implementation.
/// Satisfies the IMicCapture interface but does not capture real audio.
/// TODO(#63): Replace with platform audio implementation
/// (CoreAudio on macOS, WASAPI on Windows, ALSA on Linux, or cross-platform PortAudio).
/// </summary>
public sealed class MicCapture : IMicCapture
{
    private bool _disposed;

    public bool IsCapturing { get; private set; }

    public event Action<ReadOnlyMemory<byte>>? AudioCaptured;

    public Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsCapturing) return Task.CompletedTask;

        IsCapturing = true;
        Console.Error.WriteLine("[MicCapture] Started (stub — no real audio captured)");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsCapturing) return Task.CompletedTask;

        IsCapturing = false;
        Console.Error.WriteLine("[MicCapture] Stopped (stub)");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsCapturing = false;
    }
}
