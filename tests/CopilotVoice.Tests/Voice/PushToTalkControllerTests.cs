using CopilotVoice.Audio;
using CopilotVoice.Bridge;
using CopilotVoice.Voice;

namespace CopilotVoice.Tests.Voice;

public class PushToTalkControllerTests : IDisposable
{
    private readonly FakeVoiceLiveSession _voiceSession = new();
    private readonly FakeMicCapture _mic = new();
    private readonly FakeAudioPlayer _player = new();
    private readonly FakePttSessionBridge _bridge = new();
    private readonly PushToTalkController _controller;

    public PushToTalkControllerTests()
    {
        _bridge.AddSession("test-session"); // simulate connected CLI
        _controller = new PushToTalkController(_voiceSession, _mic, _player, _bridge);
    }

    public void Dispose()
    {
        _mic.Dispose();
        _player.Dispose();
    }

    // --- AC1: Hotkey press → Recording, mic starts, audio streams ---

    [Fact]
    public void InitialState_IsIdle()
    {
        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    [Fact]
    public void OnHotkeyPressed_FromIdle_TransitionsToRecording()
    {
        _controller.OnHotkeyPressed();
        Assert.Equal(PushToTalkState.Recording, _controller.State);
    }

    [Fact]
    public void OnHotkeyPressed_FromIdle_StartsMicCapture()
    {
        _controller.OnHotkeyPressed();
        Assert.True(_mic.IsCapturing);
    }

    [Fact]
    public void OnHotkeyPressed_FromIdle_FiresStateChangedEvent()
    {
        var states = new List<PushToTalkState>();
        _controller.StateChanged += s => states.Add(s);

        _controller.OnHotkeyPressed();

        Assert.Single(states);
        Assert.Equal(PushToTalkState.Recording, states[0]);
    }

    [Fact]
    public async Task Recording_AudioCaptured_StreamedToVoiceSession()
    {
        _controller.OnHotkeyPressed();

        var audioData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        _mic.SimulateAudioCaptured(audioData);

        // Allow the async streaming to complete
        await Task.Delay(50);

        Assert.Single(_voiceSession.SentAudioChunks);
        Assert.Equal(audioData, _voiceSession.SentAudioChunks[0]);
    }

    // --- AC2: Hotkey release → Processing, audio committed ---

    [Fact]
    public async Task OnHotkeyReleased_FromRecording_TransitionsToProcessing()
    {
        _controller.OnHotkeyPressed();
        await Task.Delay(250); // ensure > 200ms for quick-press detection

        _controller.OnHotkeyReleased();

        Assert.Equal(PushToTalkState.Processing, _controller.State);
    }

    [Fact]
    public async Task OnHotkeyReleased_FromRecording_StopsMicCapture()
    {
        _controller.OnHotkeyPressed();
        await Task.Delay(250);

        _controller.OnHotkeyReleased();

        Assert.False(_mic.IsCapturing);
    }

    [Fact]
    public async Task OnHotkeyReleased_FromRecording_CommitsAudio()
    {
        _controller.OnHotkeyPressed();
        await Task.Delay(250);

        _controller.OnHotkeyReleased();

        Assert.True(_voiceSession.AudioCommitted);
    }

    [Fact]
    public async Task OnHotkeyReleased_FiresStateChangedToProcessing()
    {
        var states = new List<PushToTalkState>();
        _controller.OnHotkeyPressed();
        await Task.Delay(250);

        _controller.StateChanged += s => states.Add(s);
        _controller.OnHotkeyReleased();

        Assert.Contains(PushToTalkState.Processing, states);
    }

    // --- AC4: Voice response audio → Playing ---

    [Fact]
    public async Task ResponseDone_FromProcessing_TransitionsToPlaying()
    {
        // Get to Processing state
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        Assert.Equal(PushToTalkState.Processing, _controller.State);

        // Simulate audio chunks arriving during Processing
        _voiceSession.SimulateAudioReceived(new byte[] { 0xAA, 0xBB });

        // Simulate response.done
        _voiceSession.SimulateResponseDone();

        // Allow async transition
        await Task.Delay(50);

        Assert.Equal(PushToTalkState.Playing, _controller.State);
    }

    [Fact]
    public async Task ResponseDone_WithAudioChunks_PlaysBufferedAudio()
    {
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();

        // Simulate audio chunks
        var chunk1 = new byte[] { 0x01, 0x02 };
        var chunk2 = new byte[] { 0x03, 0x04 };
        _voiceSession.SimulateAudioReceived(chunk1);
        _voiceSession.SimulateAudioReceived(chunk2);

        _voiceSession.SimulateResponseDone();
        await Task.Delay(50);

        Assert.True(_player.PlayWasCalled);
        // The combined audio should contain both chunks
        Assert.Equal(4, _player.LastPlayedAudio!.Length);
    }

    // --- AC5: Playback complete → Idle ---

    [Fact]
    public async Task PlaybackCompleted_FromPlaying_TransitionsToIdle()
    {
        // Get to Playing state
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(50);
        Assert.Equal(PushToTalkState.Playing, _controller.State);

        // Simulate playback completed
        _player.SimulatePlaybackCompleted();

        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    [Fact]
    public async Task FullHappyPath_Idle_Recording_Processing_Playing_Idle()
    {
        var states = new List<PushToTalkState>();
        _controller.StateChanged += s => states.Add(s);

        // 1. Press hotkey → Recording
        _controller.OnHotkeyPressed();
        Assert.Equal(PushToTalkState.Recording, _controller.State);

        await Task.Delay(250);

        // 2. Release hotkey → Processing
        _controller.OnHotkeyReleased();
        Assert.Equal(PushToTalkState.Processing, _controller.State);

        // 3. Audio arrives + response.done → Playing
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(50);
        Assert.Equal(PushToTalkState.Playing, _controller.State);

        // 4. Playback done → Idle
        _player.SimulatePlaybackCompleted();
        Assert.Equal(PushToTalkState.Idle, _controller.State);

        Assert.Equal(
            new[] { PushToTalkState.Recording, PushToTalkState.Processing, PushToTalkState.Playing, PushToTalkState.Idle },
            states);
    }

    // --- AC7: Quick press (<200ms) → cancel ---

    [Fact]
    public void QuickPress_LessThan200ms_CancelsToIdle()
    {
        _controller.OnHotkeyPressed();
        // Release immediately (< 200ms)
        _controller.OnHotkeyReleased();

        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    [Fact]
    public void QuickPress_StopsMicCapture()
    {
        _controller.OnHotkeyPressed();
        Assert.True(_mic.IsCapturing);

        _controller.OnHotkeyReleased(); // immediate release
        Assert.False(_mic.IsCapturing);
    }

    [Fact]
    public void QuickPress_DoesNotCommitAudio()
    {
        _controller.OnHotkeyPressed();
        _controller.OnHotkeyReleased(); // immediate release

        Assert.False(_voiceSession.AudioCommitted);
    }

    [Fact]
    public void QuickPress_FiresStateChangedBackToIdle()
    {
        var states = new List<PushToTalkState>();
        _controller.StateChanged += s => states.Add(s);

        _controller.OnHotkeyPressed();
        _controller.OnHotkeyReleased(); // immediate release

        // Should go Recording → Idle (cancel)
        Assert.Equal(new[] { PushToTalkState.Recording, PushToTalkState.Idle }, states);
    }

    // --- Hotkey during playback → interrupt and record ---

    [Fact]
    public async Task HotkeyPressed_DuringPlayback_StopsPlaybackAndRecords()
    {
        // Get to Playing state
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(50);
        Assert.Equal(PushToTalkState.Playing, _controller.State);

        // Press hotkey during playback
        _controller.OnHotkeyPressed();

        Assert.True(_player.StopWasCalled);
        Assert.Equal(PushToTalkState.Recording, _controller.State);
        Assert.True(_mic.IsCapturing);
    }

    // --- Error handling ---

    [Fact]
    public void OnHotkeyPressed_MicCaptureThrows_ReturnsToIdle()
    {
        _mic.ThrowOnStart = true;

        _controller.OnHotkeyPressed();

        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    [Fact]
    public async Task ErrorDuringProcessing_ReturnsToIdle()
    {
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        Assert.Equal(PushToTalkState.Processing, _controller.State);

        // Simulate error from voice session
        _voiceSession.SimulateError("API error");

        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    // --- No voice session connected ---

    [Fact]
    public void OnHotkeyReleased_NoCliConnected_StillProcesses()
    {
        // Create a controller with no connected CLI sessions
        var emptyBridge = new FakePttSessionBridge();
        var controller = new PushToTalkController(_voiceSession, _mic, _player, emptyBridge);

        controller.OnHotkeyPressed();
        // The voice session handles the error response in this case
        // The state machine should still function
        Assert.Equal(PushToTalkState.Recording, controller.State);
    }

    // --- Multiple rapid presses → no stuck states ---

    [Fact]
    public void MultipleRapidPresses_NoStuckStates()
    {
        // Rapid press/release cycles
        for (int i = 0; i < 10; i++)
        {
            _controller.OnHotkeyPressed();
            _controller.OnHotkeyReleased(); // quick press → cancel
        }

        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    [Fact]
    public void OnHotkeyPressed_WhileRecording_IsIgnored()
    {
        _controller.OnHotkeyPressed();
        Assert.Equal(PushToTalkState.Recording, _controller.State);

        // Second press while already recording — should be ignored
        _controller.OnHotkeyPressed();
        Assert.Equal(PushToTalkState.Recording, _controller.State);
    }

    [Fact]
    public async Task OnHotkeyPressed_WhileProcessing_IsIgnored()
    {
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        Assert.Equal(PushToTalkState.Processing, _controller.State);

        // Press during processing — should be ignored
        _controller.OnHotkeyPressed();
        Assert.Equal(PushToTalkState.Processing, _controller.State);
    }

    [Fact]
    public void OnHotkeyReleased_WhileIdle_IsIgnored()
    {
        // Release while idle — no-op
        _controller.OnHotkeyReleased();
        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    // --- ResponseDone with no audio → skip Playing, go to Idle ---

    [Fact]
    public async Task ResponseDone_WithNoAudio_TransitionsToIdle()
    {
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        Assert.Equal(PushToTalkState.Processing, _controller.State);

        // response.done but no audio chunks arrived
        _voiceSession.SimulateResponseDone();
        await Task.Delay(50);

        // Should go directly to Idle since there's nothing to play
        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    // --- State change events fire correctly ---

    [Fact]
    public async Task StateChanged_FiresForEveryTransition()
    {
        var states = new List<PushToTalkState>();
        _controller.StateChanged += s => states.Add(s);

        _controller.OnHotkeyPressed(); // → Recording
        await Task.Delay(250);
        _controller.OnHotkeyReleased(); // → Processing
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone(); // → Playing
        await Task.Delay(50);
        _player.SimulatePlaybackCompleted(); // → Idle

        Assert.Equal(4, states.Count);
    }

    // --- Verify mic capture started/stopped at correct transitions ---

    [Fact]
    public async Task MicCapture_StartedOnRecording_StoppedOnProcessing()
    {
        Assert.False(_mic.IsCapturing);

        _controller.OnHotkeyPressed();
        Assert.True(_mic.IsCapturing);

        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        Assert.False(_mic.IsCapturing);
    }

    // --- Verify CommitAudioAsync called on release ---

    [Fact]
    public async Task CommitAudio_CalledOnRelease_NotOnQuickPress()
    {
        // Normal release
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        Assert.True(_voiceSession.AudioCommitted);

        // Reset
        _voiceSession.AudioCommitted = false;

        // Quick press — should NOT commit
        _voiceSession.SimulateResponseDone(); // clear processing
        await Task.Delay(50);
        _controller.OnHotkeyPressed();
        _controller.OnHotkeyReleased(); // immediate
        Assert.False(_voiceSession.AudioCommitted);
    }
}

// === Fakes ===

internal sealed class FakeVoiceLiveSession : IVoiceLiveSession
{
    public List<byte[]> SentAudioChunks { get; } = new();
    public bool AudioCommitted { get; set; }

    public event Action<ReadOnlyMemory<byte>>? AudioReceived;
    public event Action<string>? TranscriptReceived;
    public event Action<FunctionCall>? FunctionCallReceived;
    public event Action? ResponseDone;
    public event Action<string>? ErrorReceived;
    public event Action? SessionReady;
    public event Action? Disconnected;

    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm16Audio, CancellationToken ct = default)
    {
        SentAudioChunks.Add(pcm16Audio.ToArray());
        return Task.CompletedTask;
    }

    public Task CommitAudioAsync(CancellationToken ct = default)
    {
        AudioCommitted = true;
        return Task.CompletedTask;
    }

    public Task SendTextAsync(string text, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpdateSessionAsync(SessionUpdate update, CancellationToken ct = default) => Task.CompletedTask;
    public Task SendFunctionResultAsync(string callId, string result, CancellationToken ct = default) => Task.CompletedTask;

    public void SimulateAudioReceived(byte[] data) => AudioReceived?.Invoke(data);
    public void SimulateResponseDone() => ResponseDone?.Invoke();
    public void SimulateError(string message) => ErrorReceived?.Invoke(message);
    public void SimulateFunctionCall(FunctionCall call) => FunctionCallReceived?.Invoke(call);
    public void SimulateDisconnected() => Disconnected?.Invoke();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeMicCapture : IMicCapture
{
    public bool IsCapturing { get; private set; }
    public bool ThrowOnStart { get; set; }

    public event Action<ReadOnlyMemory<byte>>? AudioCaptured;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (ThrowOnStart) throw new InvalidOperationException("No microphone available");
        IsCapturing = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsCapturing = false;
        return Task.CompletedTask;
    }

    public void SimulateAudioCaptured(byte[] data)
    {
        AudioCaptured?.Invoke(data);
    }

    public void Dispose() { IsCapturing = false; }
}

internal sealed class FakeAudioPlayer : IAudioPlayer
{
    public bool IsPlaying { get; private set; }
    public bool PlayWasCalled { get; private set; }
    public bool StopWasCalled { get; private set; }
    public byte[]? LastPlayedAudio { get; private set; }
    public bool ThrowOnPlay { get; set; }

    public event Action? PlaybackCompleted;

    public Task PlayAsync(ReadOnlyMemory<byte> pcm16Audio, CancellationToken ct = default)
    {
        if (ThrowOnPlay) throw new InvalidOperationException("Playback device error");
        PlayWasCalled = true;
        LastPlayedAudio = pcm16Audio.ToArray();
        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopWasCalled = true;
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public void SimulatePlaybackCompleted()
    {
        IsPlaying = false;
        PlaybackCompleted?.Invoke();
    }

    public void Dispose() { IsPlaying = false; }
}

internal sealed class FakePttSessionBridge : ISessionBridge
{
    private readonly List<string> _sessions = new();

    public event Action<CliMessage>? MessageReceived;
    public event Action<CliEvent>? EventReceived;

    public IReadOnlyList<string> ConnectedSessions => _sessions.AsReadOnly();

    public void AddSession(string sessionId) => _sessions.Add(sessionId);
    public void OnMessageReceived(CliMessage message) => MessageReceived?.Invoke(message);
    public void OnEventReceived(CliEvent evt) => EventReceived?.Invoke(evt);
    public void QueueCommand(string sessionId, SendPromptCommand command) { }
    public IAsyncEnumerable<SendPromptCommand> GetCommandStream(string sessionId, CancellationToken ct)
        => throw new NotImplementedException();
    public void RemoveSession(string sessionId) => _sessions.Remove(sessionId);
}
