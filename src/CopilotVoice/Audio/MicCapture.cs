using System.Runtime.InteropServices;
using PortAudioSharp;

namespace CopilotVoice.Audio;

/// <summary>
/// Captures microphone audio in PCM16 format (16 kHz, mono) using PortAudio.
/// Start/Stop are idempotent — calling Start while already capturing is a no-op.
/// Thread-safe: the PortAudio callback fires from an audio thread; captured
/// data is copied and delivered via the <see cref="AudioCaptured"/> event.
/// </summary>
public sealed class MicCapture : IMicCapture
{
    /// <summary>Sample rate required by Azure Voice Live API.</summary>
    private const int SampleRate = 16000;

    /// <summary>Mono channel.</summary>
    private const int ChannelCount = 1;

    /// <summary>
    /// Frames per callback buffer: 1600 samples = 100 ms at 16 kHz.
    /// Balances low latency with reasonable callback overhead.
    /// </summary>
    private const uint FramesPerBuffer = 1600;

    /// <summary>Bytes per PCM16 sample.</summary>
    private const int BytesPerSample = 2;

    private readonly object _lock = new();
    private PortAudioSharp.Stream? _stream;
    private bool _disposed;
    private volatile bool _isCapturing;
    private bool _paInitialized;

    public bool IsCapturing => _isCapturing;

    public event Action<ReadOnlyMemory<byte>>? AudioCaptured;

    /// <summary>Begin capturing audio from the default microphone.</summary>
    /// <exception cref="InvalidOperationException">No microphone is available.</exception>
    public Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_isCapturing) return Task.CompletedTask;

        lock (_lock)
        {
            if (_isCapturing) return Task.CompletedTask;

            PortAudioLifecycle.EnsureInitialized();
            _paInitialized = true;

            var inputDevice = PortAudioSharp.PortAudio.DefaultInputDevice;
            if (inputDevice == PortAudioSharp.PortAudio.NoDevice)
            {
                PortAudioLifecycle.Release();
                _paInitialized = false;
                throw new InvalidOperationException(
                    "No microphone available. Check system audio settings.");
            }

            var deviceInfo = PortAudioSharp.PortAudio.GetDeviceInfo(inputDevice);
            var inputParams = new StreamParameters
            {
                device = inputDevice,
                channelCount = ChannelCount,
                sampleFormat = SampleFormat.Int16,
                suggestedLatency = deviceInfo.defaultLowInputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero,
            };

            _stream = new PortAudioSharp.Stream(
                inParams: inputParams,
                outParams: null,
                sampleRate: SampleRate,
                framesPerBuffer: FramesPerBuffer,
                streamFlags: StreamFlags.ClipOff,
                callback: OnInputCallback,
                userData: null!);

            _stream.Start();
            _isCapturing = true;

            Console.Error.WriteLine(
                $"[MicCapture] Started — device: {deviceInfo.name}, rate: {SampleRate} Hz");
        }

        return Task.CompletedTask;
    }

    /// <summary>Stop capturing audio. Idempotent — safe to call when not capturing.</summary>
    public Task StopAsync()
    {
        if (!_isCapturing) return Task.CompletedTask;

        lock (_lock)
        {
            if (!_isCapturing) return Task.CompletedTask;

            _isCapturing = false;
            CleanupStream();

            Console.Error.WriteLine("[MicCapture] Stopped");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// PortAudio input callback — fires from the audio thread.
    /// Copies PCM16 data into a managed byte array and raises <see cref="AudioCaptured"/>.
    /// </summary>
    private StreamCallbackResult OnInputCallback(
        IntPtr input,
        IntPtr output,
        uint frameCount,
        ref StreamCallbackTimeInfo timeInfo,
        StreamCallbackFlags statusFlags,
        IntPtr userData)
    {
        if (!_isCapturing || input == IntPtr.Zero)
            return StreamCallbackResult.Continue;

        int byteCount = (int)(frameCount * ChannelCount * BytesPerSample);
        var buffer = new byte[byteCount];
        Marshal.Copy(input, buffer, 0, byteCount);

        // Fire event — subscribers should not block the audio thread.
        AudioCaptured?.Invoke(buffer);

        return StreamCallbackResult.Continue;
    }

    /// <summary>Releases the PortAudio stream and PA reference.</summary>
    private void CleanupStream()
    {
        if (_stream is not null)
        {
            try { _stream.Abort(); } catch { /* stream may already be stopped */ }
            try { _stream.Dispose(); } catch { /* best effort */ }
            _stream = null;
        }

        if (_paInitialized)
        {
            PortAudioLifecycle.Release();
            _paInitialized = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isCapturing = false;
        CleanupStream();
    }
}
