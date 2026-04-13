using CopilotVoice.Audio;
using CopilotVoice.Bridge;

namespace CopilotVoice.Voice;

/// <summary>
/// Talk Mode state values.
/// </summary>
public enum TalkModeState
{
    /// <summary>Talk Mode disabled. Mic is off.</summary>
    Off,
    /// <summary>Mic open, streaming audio to Voice Live API. Waiting for speech.</summary>
    Listening,
    /// <summary>Speech detected by API. Mic muted, buffering response audio.</summary>
    Processing,
    /// <summary>Response audio playing. Mic muted.</summary>
    Speaking
}

/// <summary>
/// Contract for continuous bidirectional voice conversation control.
/// </summary>
public interface ITalkModeController
{
    /// <summary>Current state of the Talk Mode state machine.</summary>
    TalkModeState State { get; }

    /// <summary>Whether Talk Mode is currently active (not Off).</summary>
    bool IsActive { get; }

    /// <summary>Fired on every state transition.</summary>
    event Action<TalkModeState>? StateChanged;

    /// <summary>Activate Talk Mode: open mic continuously, begin conversation loop.</summary>
    Task ActivateAsync(CancellationToken ct = default);

    /// <summary>Deactivate Talk Mode: stop mic, stop playback, return to Off.</summary>
    Task DeactivateAsync();
}

/// <summary>
/// Continuous bidirectional voice conversation state machine.
/// Off → Listening → Processing → Speaking → Listening (loop).
/// Uses server-side VAD (Voice Live API detects speech boundaries).
/// Coordinates with TurnManager for echo-free turn transitions.
/// </summary>
public sealed class TalkModeController : ITalkModeController, IDisposable
{
    private readonly IVoiceLiveSession _voiceSession;
    private readonly IMicCapture _micCapture;
    private readonly IAudioPlayer _audioPlayer;
    private readonly ISessionBridge _sessionBridge;
    private readonly TurnManager _turnManager;
    private readonly object _stateLock = new();
    private readonly List<byte> _audioBuffer = new();

    private TalkModeState _state = TalkModeState.Off;
    private CancellationTokenSource? _sessionCts;
    private bool _disposed;
    private volatile bool _activating;

    /// <summary>Maximum audio buffer size (10 MB) to prevent unbounded memory growth.</summary>
    private const int MaxAudioBufferBytes = 10 * 1024 * 1024;

    public TalkModeState State
    {
        get { lock (_stateLock) return _state; }
    }

    public bool IsActive => _activating || State != TalkModeState.Off;

    public event Action<TalkModeState>? StateChanged;

    public TalkModeController(
        IVoiceLiveSession voiceSession,
        IMicCapture micCapture,
        IAudioPlayer audioPlayer,
        ISessionBridge sessionBridge,
        TurnManager turnManager)
    {
        _voiceSession = voiceSession ?? throw new ArgumentNullException(nameof(voiceSession));
        _micCapture = micCapture ?? throw new ArgumentNullException(nameof(micCapture));
        _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
        _sessionBridge = sessionBridge ?? throw new ArgumentNullException(nameof(sessionBridge));
        _turnManager = turnManager ?? throw new ArgumentNullException(nameof(turnManager));
    }

    /// <inheritdoc />
    public async Task ActivateAsync(CancellationToken ct = default)
    {
        lock (_stateLock)
        {
            if (_state != TalkModeState.Off || _activating)
                return;
            _activating = true;
        }

        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        WireEvents();

        try
        {
            await _micCapture.StartAsync(_sessionCts.Token).ConfigureAwait(false);
            lock (_stateLock)
            {
                SetState(TalkModeState.Listening);
            }
        }
        catch (Exception ex)
        {
            Log($"Activation failed: {ex.Message}");
            UnwireEvents();
            _sessionCts?.Dispose();
            _sessionCts = null;
        }
        finally
        {
            _activating = false;
        }
    }

    /// <inheritdoc />
    public async Task DeactivateAsync()
    {
        TalkModeState previousState;
        lock (_stateLock)
        {
            if (_state == TalkModeState.Off)
                return;

            previousState = _state;
            SetState(TalkModeState.Off);
        }

        UnwireEvents();

        // Clean up based on previous state
        if (previousState == TalkModeState.Listening || previousState == TalkModeState.Processing)
        {
            await StopMicSafeAsync().ConfigureAwait(false);
        }
        if (previousState == TalkModeState.Speaking)
        {
            await StopPlaybackSafeAsync().ConfigureAwait(false);
        }

        lock (_stateLock)
        {
            _audioBuffer.Clear();
        }

        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _sessionCts = null;
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

    // --- Event handlers ---

    private void OnAudioCaptured(ReadOnlyMemory<byte> audio)
    {
        if (State != TalkModeState.Listening)
            return;

        _ = SendAudioSafeAsync(audio);
    }

    private async Task SendAudioSafeAsync(ReadOnlyMemory<byte> audio)
    {
        try
        {
            await _voiceSession.SendAudioAsync(audio).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log($"SendAudio failed: {ex.Message}");
        }
    }

    private void OnVoiceAudioReceived(ReadOnlyMemory<byte> audio)
    {
        lock (_stateLock)
        {
            if (_state == TalkModeState.Listening)
            {
                // First audio chunk — API detected speech end, started response
                SetState(TalkModeState.Processing);
                _ = Task.Run(() => MuteMicSafeAsync());
            }

            if (_state == TalkModeState.Processing)
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
        _ = Task.Run(() => TransitionToSpeaking());
    }

    private void TransitionToSpeaking()
    {
        byte[] audioToPlay;
        lock (_stateLock)
        {
            if (_state != TalkModeState.Processing)
                return;

            if (_audioBuffer.Count == 0)
            {
                // No audio to play — resume listening
                SetState(TalkModeState.Listening);
                _ = Task.Run(() => UnmuteMicSafeAsync());
                return;
            }

            audioToPlay = _audioBuffer.ToArray();
            _audioBuffer.Clear();
            SetState(TalkModeState.Speaking);
        }

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
            await ResumeListeningAsync().ConfigureAwait(false);
        }
    }

    private void OnPlaybackCompleted()
    {
        lock (_stateLock)
        {
            if (_state != TalkModeState.Speaking)
                return;

            SetState(TalkModeState.Listening);
        }

        // Unmute mic after post-playback delay (outside lock — async)
        _ = Task.Run(() => UnmuteMicSafeAsync());
    }

    private void OnVoiceError(string error)
    {
        lock (_stateLock)
        {
            if (_state == TalkModeState.Off)
                return;

            Log($"Voice API error: {error}");

            if (_state == TalkModeState.Processing || _state == TalkModeState.Speaking)
            {
                _audioBuffer.Clear();
                SetState(TalkModeState.Listening);
                _ = Task.Run(() => UnmuteMicSafeAsync());
            }
            // If already Listening, stay Listening — don't exit Talk Mode
        }
    }

    private void OnVoiceDisconnected()
    {
        _ = Task.Run(async () =>
        {
            Log("Voice session disconnected, deactivating Talk Mode");
            await DeactivateAsync().ConfigureAwait(false);
        });
    }

    // --- Helpers ---

    private async Task ResumeListeningAsync()
    {
        lock (_stateLock)
        {
            if (_state == TalkModeState.Off)
                return;
            _audioBuffer.Clear();
            SetState(TalkModeState.Listening);
        }

        await UnmuteMicSafeAsync().ConfigureAwait(false);
    }

    private async Task MuteMicSafeAsync()
    {
        try
        {
            await _turnManager.MuteMicAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log($"Mic mute error: {ex.Message}");
        }
    }

    private async Task UnmuteMicSafeAsync()
    {
        try
        {
            var ct = _sessionCts?.Token ?? CancellationToken.None;
            await _turnManager.UnmuteMicAfterDelayAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Session was deactivated during delay — expected
        }
        catch (Exception ex)
        {
            Log($"Mic unmute error: {ex.Message}");
        }
    }

    private async Task StopMicSafeAsync()
    {
        try { await _micCapture.StopAsync().ConfigureAwait(false); }
        catch (Exception ex) { Log($"Mic stop error: {ex.Message}"); }
    }

    private async Task StopPlaybackSafeAsync()
    {
        try { await _audioPlayer.StopAsync().ConfigureAwait(false); }
        catch (Exception ex) { Log($"Playback stop error: {ex.Message}"); }
    }

    private void SetState(TalkModeState newState)
    {
        _state = newState;
        StateChanged?.Invoke(newState);
    }

    private static void Log(string message)
    {
        Console.Error.WriteLine($"[TalkMode] {message}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnwireEvents();
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
    }
}
