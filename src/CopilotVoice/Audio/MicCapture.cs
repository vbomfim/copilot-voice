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
    /// <summary>Capture at 48kHz (widely supported), resample to 24kHz for API.</summary>
    private const int CaptureSampleRate = 48000;

    /// <summary>API expects 24kHz PCM16.</summary>
    private const int ApiSampleRate = 24000;

    /// <summary>Mono channel.</summary>
    private const int ChannelCount = 1;

    /// <summary>
    /// Frames per callback buffer: 4800 samples = 100 ms at 48 kHz.
    /// Balances low latency with reasonable callback overhead.
    /// </summary>
    private const uint FramesPerBuffer = 4800;

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
                sampleRate: CaptureSampleRate,
                framesPerBuffer: FramesPerBuffer,
                streamFlags: StreamFlags.ClipOff,
                callback: OnInputCallback,
                userData: null!);

            _stream.Start();
            _isCapturing = true;

            // Register cancellation to stop capture
            if (ct.CanBeCanceled)
                ct.Register(() => _ = StopAsync());

            Console.Error.WriteLine(
                $"[MicCapture] Started — device: {deviceInfo.name}, capture: {CaptureSampleRate} Hz, API: {ApiSampleRate} Hz");
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
    private int _callbackCount;

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

        int capturedBytes = (int)(frameCount * ChannelCount * BytesPerSample);
        var capturedBuffer = new byte[capturedBytes];
        Marshal.Copy(input, capturedBuffer, 0, capturedBytes);

        // Downsample from 48kHz to 24kHz: take every other sample (2:1 ratio)
        int resampledSamples = (int)frameCount / 2;
        var resampledBuffer = new byte[resampledSamples * BytesPerSample];
        for (int i = 0; i < resampledSamples; i++)
        {
            int srcOffset = i * 2 * BytesPerSample; // skip every other sample
            int dstOffset = i * BytesPerSample;
            resampledBuffer[dstOffset] = capturedBuffer[srcOffset];
            resampledBuffer[dstOffset + 1] = capturedBuffer[srcOffset + 1];
        }

        _callbackCount++;
        if (_callbackCount <= 3)
        {
            int nonZero = 0;
            for (int i = 0; i < Math.Min(resampledBuffer.Length, 100); i++)
                if (resampledBuffer[i] != 0) nonZero++;
            Console.Error.WriteLine($"[MicCapture] Callback #{_callbackCount}: {resampledBuffer.Length} bytes (resampled from {capturedBytes}), nonZero={nonZero}/100");
        }

        AudioCaptured?.Invoke(resampledBuffer);

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
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _isCapturing = false;
            CleanupStream();
        }
    }
}
