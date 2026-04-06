using System.Runtime.InteropServices;
using PortAudioSharp;

namespace CopilotVoice.Audio;

/// <summary>
/// Plays PCM16 audio data (16 kHz, mono) through the default audio output using PortAudio.
/// <para>
/// <see cref="PlayAsync"/> streams audio via a callback and awaits natural completion.
/// <see cref="StopAsync"/> aborts playback immediately without firing <see cref="PlaybackCompleted"/>.
/// All public operations are idempotent and thread-safe.
/// </para>
/// </summary>
public sealed class AudioPlayer : IAudioPlayer
{
    /// <summary>Sample rate required by Azure Voice Live API.</summary>
    private const int SampleRate = 16000;

    /// <summary>Mono channel.</summary>
    private const int ChannelCount = 1;

    /// <summary>
    /// Frames per callback buffer: 1600 samples = 100 ms at 16 kHz.
    /// </summary>
    private const uint FramesPerBuffer = 1600;

    /// <summary>Bytes per PCM16 sample.</summary>
    private const int BytesPerSample = 2;

    private readonly object _lock = new();
    private PortAudioSharp.Stream? _stream;
    private bool _disposed;
    private volatile bool _isPlaying;
    private bool _paInitialized;

    // Playback state — accessed from the audio callback thread
    private byte[]? _audioData;
    private int _playbackPosition;
    private volatile bool _stopRequested;
    private TaskCompletionSource? _playbackTcs;

    public bool IsPlaying => _isPlaying;

    public event Action? PlaybackCompleted;

    /// <summary>
    /// Play a buffer of PCM16 audio (16 kHz, mono) through the default output device.
    /// Blocks until playback finishes naturally, is stopped, or is cancelled.
    /// </summary>
    /// <exception cref="InvalidOperationException">No audio output device is available.</exception>
    public async Task PlayAsync(ReadOnlyMemory<byte> pcm16Audio, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Handle empty data — nothing to play
        if (pcm16Audio.Length == 0)
        {
            PlaybackCompleted?.Invoke();
            return;
        }

        // Stop any current playback first (no PlaybackCompleted for interrupted playback)
        await StopAsync();

        TaskCompletionSource tcs;

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            PortAudioLifecycle.EnsureInitialized();
            _paInitialized = true;

            var outputDevice = PortAudioSharp.PortAudio.DefaultOutputDevice;
            if (outputDevice == PortAudioSharp.PortAudio.NoDevice)
            {
                PortAudioLifecycle.Release();
                _paInitialized = false;
                throw new InvalidOperationException(
                    "No audio output device available. Check system audio settings.");
            }

            _audioData = pcm16Audio.ToArray();
            _playbackPosition = 0;
            _stopRequested = false;
            tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _playbackTcs = tcs;

            var deviceInfo = PortAudioSharp.PortAudio.GetDeviceInfo(outputDevice);
            var outputParams = new StreamParameters
            {
                device = outputDevice,
                channelCount = ChannelCount,
                sampleFormat = SampleFormat.Int16,
                suggestedLatency = deviceInfo.defaultLowOutputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero,
            };

            _stream = new PortAudioSharp.Stream(
                inParams: null,
                outParams: outputParams,
                sampleRate: SampleRate,
                framesPerBuffer: FramesPerBuffer,
                streamFlags: StreamFlags.NoFlag,
                callback: OnOutputCallback,
                userData: null!);

            _isPlaying = true;
            _stream.Start();

            Console.Error.WriteLine(
                $"[AudioPlayer] Playing {pcm16Audio.Length} bytes ({pcm16Audio.Length / (SampleRate * BytesPerSample * ChannelCount)} s)");
        }

        // Register cancellation to trigger StopAsync
        using var reg = ct.Register(() => _ = StopAsync());

        await tcs.Task;
    }

    /// <summary>Stop any active playback immediately. Does NOT fire PlaybackCompleted.</summary>
    public Task StopAsync()
    {
        if (!_isPlaying) return Task.CompletedTask;

        PortAudioSharp.Stream? streamToCleanup;
        bool paWasInitialized = false;

        lock (_lock)
        {
            if (!_isPlaying) return Task.CompletedTask;

            _stopRequested = true;
            _isPlaying = false;
            _audioData = null;

            streamToCleanup = _stream;
            _stream = null;

            _playbackTcs?.TrySetResult();
            _playbackTcs = null;

            // Release PA ref inside lock to prevent double-release race with SignalNaturalCompletion
            if (_paInitialized)
            {
                _paInitialized = false;
                paWasInitialized = true;
            }
        }

        // Cleanup stream OUTSIDE the lock to avoid deadlock with PortAudio callbacks.
        if (streamToCleanup is not null)
        {
            try { streamToCleanup.Abort(); } catch { /* stream may already be stopped */ }
            try { streamToCleanup.Dispose(); } catch { /* best effort */ }
        }

        if (paWasInitialized)
            PortAudioLifecycle.Release();

        Console.Error.WriteLine("[AudioPlayer] Stopped (interrupted)");

        return Task.CompletedTask;
    }

    /// <summary>
    /// PortAudio output callback — fires from the audio thread.
    /// Copies PCM16 data from the buffer into the output and signals
    /// completion when all data has been written.
    /// </summary>
    private StreamCallbackResult OnOutputCallback(
        IntPtr input,
        IntPtr output,
        uint frameCount,
        ref StreamCallbackTimeInfo timeInfo,
        StreamCallbackFlags statusFlags,
        IntPtr userData)
    {
        var data = _audioData; // capture local to avoid TOCTOU race
        if (output == IntPtr.Zero || data is null || _stopRequested)
        {
            if (output != IntPtr.Zero)
                ClearBuffer(output, (int)(frameCount * ChannelCount * BytesPerSample));
            return StreamCallbackResult.Abort;
        }

        int byteCount = (int)(frameCount * ChannelCount * BytesPerSample);
        int remaining = data.Length - _playbackPosition;

        if (remaining <= 0)
        {
            ClearBuffer(output, byteCount);
            SignalNaturalCompletion();
            return StreamCallbackResult.Complete;
        }

        int toCopy = Math.Min(byteCount, remaining);
        Marshal.Copy(data, _playbackPosition, output, toCopy);
        _playbackPosition += toCopy;

        if (toCopy < byteCount)
        {
            ClearBuffer(output + toCopy, byteCount - toCopy);
        }

        if (_playbackPosition >= data.Length)
        {
            SignalNaturalCompletion();
            return StreamCallbackResult.Complete;
        }

        return StreamCallbackResult.Continue;
    }

    /// <summary>
    /// Called from the audio callback thread when playback finishes naturally
    /// (all data has been written). Schedules cleanup and fires PlaybackCompleted.
    /// </summary>
    private void SignalNaturalCompletion()
    {
        // Use ThreadPool to avoid doing heavy work on the audio thread
        // and to avoid calling PortAudio functions from within the callback.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            PortAudioSharp.Stream? streamToCleanup;
            bool paWasInit = false;

            lock (_lock)
            {
                if (_stopRequested)
                    return; // StopAsync already handled cleanup

                _isPlaying = false;
                _audioData = null;

                streamToCleanup = _stream;
                _stream = null;

                _playbackTcs?.TrySetResult();
                _playbackTcs = null;

                if (_paInitialized)
                {
                    _paInitialized = false;
                    paWasInit = true;
                }
            }

            // Cleanup outside lock
            if (streamToCleanup is not null)
            {
                try { streamToCleanup.Dispose(); } catch { /* best effort */ }
            }

            if (paWasInit)
                PortAudioLifecycle.Release();

            PlaybackCompleted?.Invoke();
        });
    }

    /// <summary>Writes zeros to a native buffer (silence).</summary>
    private static void ClearBuffer(IntPtr buffer, int byteCount)
    {
        var silence = new byte[byteCount];
        Marshal.Copy(silence, 0, buffer, byteCount);
    }

    public void Dispose()
    {
        PortAudioSharp.Stream? streamToCleanup;
        bool paWasInit = false;

        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _stopRequested = true;
            _isPlaying = false;

            streamToCleanup = _stream;
            _stream = null;
            _audioData = null;
            _playbackTcs?.TrySetResult();

            if (_paInitialized)
            {
                _paInitialized = false;
                paWasInit = true;
            }
        }

        if (streamToCleanup is not null)
        {
            try { streamToCleanup.Abort(); } catch { /* best effort */ }
            try { streamToCleanup.Dispose(); } catch { /* best effort */ }
        }

        if (paWasInit)
            PortAudioLifecycle.Release();
    }
}
