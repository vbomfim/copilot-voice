using CopilotVoice.Audio;
using CopilotVoice.Bridge;

namespace CopilotVoice.Voice;

/// <summary>
/// Push-to-talk state values.
/// </summary>
public enum PushToTalkState
{
    Idle,
    Recording,
    Processing,
    Playing
}

/// <summary>
/// Contract for push-to-talk coordination.
/// </summary>
public interface IPushToTalkController
{
    PushToTalkState State { get; }
    event Action<PushToTalkState>? StateChanged;
    void OnHotkeyPressed();
    void OnHotkeyReleased();
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
}

/// <summary>
/// State machine for push-to-talk: Idle → Recording → Processing → Playing → Idle.
/// Coordinates mic capture, Voice Live API streaming, and audio playback.
/// All dependencies are injected via interfaces for testability.
/// </summary>
public sealed class PushToTalkController : IPushToTalkController, IDisposable
{
    private readonly IVoiceLiveSession _voiceSession;
    private readonly IMicCapture _micCapture;
    private readonly IAudioPlayer _audioPlayer;
    private readonly ISessionBridge _sessionBridge;
    private readonly object _stateLock = new();
    private readonly List<byte> _audioBuffer = new();

    private PushToTalkState _state = PushToTalkState.Idle;
    private long _pressTimestampTicks;
    private bool _disposed;

    /// <summary>Minimum hold duration (ms) to count as a real press, not a cancel.</summary>
    private const int QuickPressCancelMs = 200;

    /// <summary>Maximum audio buffer size (10 MB) to prevent unbounded memory growth.</summary>
    private const int MaxAudioBufferBytes = 10 * 1024 * 1024;

    public PushToTalkState State
    {
        get { lock (_stateLock) return _state; }
    }

    public event Action<PushToTalkState>? StateChanged;

    public PushToTalkController(
        IVoiceLiveSession voiceSession,
        IMicCapture micCapture,
        IAudioPlayer audioPlayer,
        ISessionBridge sessionBridge)
    {
        _voiceSession = voiceSession ?? throw new ArgumentNullException(nameof(voiceSession));
        _micCapture = micCapture ?? throw new ArgumentNullException(nameof(micCapture));
        _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
        _sessionBridge = sessionBridge ?? throw new ArgumentNullException(nameof(sessionBridge));

        WireEvents();
    }

    /// <inheritdoc />
    public void OnHotkeyPressed()
    {
        lock (_stateLock)
        {
            switch (_state)
            {
                case PushToTalkState.Idle:
                    TransitionToRecording();
                    break;

                case PushToTalkState.Playing:
                    InterruptPlaybackAndRecord();
                    break;

                // Ignore press during Recording or Processing
            }
        }
    }

    /// <inheritdoc />
    public void OnHotkeyReleased()
    {
        lock (_stateLock)
        {
            if (_state != PushToTalkState.Recording)
                return;

            var holdDurationMs = GetHoldDurationMs();

            if (holdDurationMs < QuickPressCancelMs)
            {
                CancelRecording();
            }
            else
            {
                TransitionToProcessing();
            }
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct = default)
    {
        // Controller is ready once constructed and events are wired.
        // Future: could validate voice session connectivity here.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync()
    {
        lock (_stateLock)
        {
            if (_state == PushToTalkState.Recording)
            {
                StopMicSafe();
            }
            else if (_state == PushToTalkState.Playing)
            {
                StopPlaybackSafe();
            }
            else if (_state == PushToTalkState.Processing)
            {
                _audioBuffer.Clear();
            }

            SetState(PushToTalkState.Idle);
        }

        return Task.CompletedTask;
    }

    // --- State transitions ---

    private void TransitionToRecording()
    {
        _audioBuffer.Clear();
        _pressTimestampTicks = Environment.TickCount64;

        try
        {
            _micCapture.StartAsync().GetAwaiter().GetResult();
            SetState(PushToTalkState.Recording);
        }
        catch (Exception ex)
        {
            Log($"Mic capture failed: {ex.Message}");
            SetState(PushToTalkState.Idle);
        }
    }

    private void InterruptPlaybackAndRecord()
    {
        StopPlaybackSafe();
        TransitionToRecording();
    }

    private void CancelRecording()
    {
        StopMicSafe();
        SetState(PushToTalkState.Idle);
    }

    private void TransitionToProcessing()
    {
        StopMicSafe();
        SetState(PushToTalkState.Processing);

        try
        {
            _voiceSession.CommitAudioAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log($"CommitAudio failed: {ex.Message}");
            SetState(PushToTalkState.Idle);
        }
    }

    private void TransitionToPlaying()
    {
        byte[] audioToPlay;
        lock (_stateLock)
        {
            if (_state != PushToTalkState.Processing)
                return;

            if (_audioBuffer.Count == 0)
            {
                // No audio to play — go directly to Idle
                SetState(PushToTalkState.Idle);
                return;
            }

            audioToPlay = _audioBuffer.ToArray();
            _audioBuffer.Clear();
            SetState(PushToTalkState.Playing);
        }

        // Play outside lock — PlayAsync may complete synchronously in stubs
        _ = PlayAudioAsync(audioToPlay);
    }

    private async Task PlayAudioAsync(byte[] audio)
    {
        try
        {
            await _audioPlayer.PlayAsync(audio).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log($"Playback failed: {ex.Message}");
            lock (_stateLock)
            {
                SetState(PushToTalkState.Idle);
            }
        }
    }

    // --- Event wiring ---

    private void WireEvents()
    {
        _micCapture.AudioCaptured += OnAudioCaptured;
        _voiceSession.AudioReceived += OnVoiceAudioReceived;
        _voiceSession.ResponseDone += OnResponseDone;
        _voiceSession.ErrorReceived += OnVoiceError;
        _voiceSession.Disconnected += OnVoiceDisconnected;
        _audioPlayer.PlaybackCompleted += OnPlaybackCompleted;
    }

    private void UnwireEvents()
    {
        _micCapture.AudioCaptured -= OnAudioCaptured;
        _voiceSession.AudioReceived -= OnVoiceAudioReceived;
        _voiceSession.ResponseDone -= OnResponseDone;
        _voiceSession.ErrorReceived -= OnVoiceError;
        _voiceSession.Disconnected -= OnVoiceDisconnected;
        _audioPlayer.PlaybackCompleted -= OnPlaybackCompleted;
    }

    private void OnAudioCaptured(ReadOnlyMemory<byte> audio)
    {
        if (State != PushToTalkState.Recording)
            return;

        Console.Error.WriteLine($"[PushToTalk] Audio chunk: {audio.Length} bytes");
        _ = SendAudioSafeAsync(audio);
    }

    private int _sendCount;

    private async Task SendAudioSafeAsync(ReadOnlyMemory<byte> audio)
    {
        try
        {
            await _voiceSession.SendAudioAsync(audio).ConfigureAwait(false);
            var count = Interlocked.Increment(ref _sendCount);
            if (count <= 3)
                Console.Error.WriteLine($"[PushToTalk] SendAudio OK #{count}: {audio.Length} bytes");
        }
        catch (Exception ex)
        {
            Log($"SendAudio FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnVoiceAudioReceived(ReadOnlyMemory<byte> audio)
    {
        lock (_stateLock)
        {
            if (_state == PushToTalkState.Processing || _state == PushToTalkState.Playing)
            {
                if (_audioBuffer.Count + audio.Length > MaxAudioBufferBytes)
                {
                    Log("Audio buffer exceeded 10MB limit, discarding");
                    _audioBuffer.Clear();
                    return;
                }
                _audioBuffer.AddRange(audio.ToArray());
            }
        }
    }

    private void OnResponseDone()
    {
        // Use Task.Run to avoid blocking the event source
        _ = Task.Run(() => TransitionToPlaying());
    }

    private void OnVoiceError(string error)
    {
        lock (_stateLock)
        {
            if (_state == PushToTalkState.Processing)
            {
                Log($"Voice API error during processing: {error}");
                _audioBuffer.Clear();
                SetState(PushToTalkState.Idle);
            }
        }
    }

    private void OnVoiceDisconnected()
    {
        lock (_stateLock)
        {
            if (_state == PushToTalkState.Idle)
                return;

            Log("Voice session disconnected, returning to Idle");
            if (_state == PushToTalkState.Recording) StopMicSafe();
            if (_state == PushToTalkState.Playing) StopPlaybackSafe();
            _audioBuffer.Clear();
            SetState(PushToTalkState.Idle);
        }
    }

    private void OnPlaybackCompleted()
    {
        lock (_stateLock)
        {
            if (_state == PushToTalkState.Playing)
            {
                SetState(PushToTalkState.Idle);
            }
        }
    }

    // --- Helpers ---

    private void SetState(PushToTalkState newState)
    {
        _state = newState;
        // Event fired inside lock is safe for now — subscribers must not re-enter.
        // TODO: Move event outside lock if UI dispatching causes issues.
        StateChanged?.Invoke(newState);
    }

    private long GetHoldDurationMs()
    {
        return Environment.TickCount64 - _pressTimestampTicks;
    }

    private void StopMicSafe()
    {
        try { _micCapture.StopAsync().GetAwaiter().GetResult(); }
        catch (Exception ex) { Log($"Mic stop error: {ex.Message}"); }
    }

    private void StopPlaybackSafe()
    {
        try { _audioPlayer.StopAsync().GetAwaiter().GetResult(); }
        catch (Exception ex) { Log($"Playback stop error: {ex.Message}"); }
    }

    private static void Log(string message)
    {
        Console.Error.WriteLine($"[PushToTalk] {message}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnwireEvents();
    }
}
