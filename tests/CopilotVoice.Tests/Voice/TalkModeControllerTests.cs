using CopilotVoice.Audio;
using CopilotVoice.Bridge;
using CopilotVoice.Voice;

namespace CopilotVoice.Tests.Voice;

/// <summary>
/// Unit tests for TalkModeController — continuous bidirectional voice
/// conversation state machine. Tests cover:
/// - Full conversation loop (Off→Listening→Processing→Speaking→Listening)
/// - Deactivation from each state
/// - Double-tap detection (tested at AppServices level, not here)
/// - Turn management (mic mute during playback, unmute after delay)
/// - Error recovery (→ Listening, not Off)
/// - Disconnect → deactivate
/// - CLI disconnect during Talk Mode → continue voice-only
/// </summary>
public class TalkModeControllerTests : IDisposable
{
    private readonly FakeVoiceLiveSession _voiceSession = new();
    private readonly FakeMicCapture _mic = new();
    private readonly FakeAudioPlayer _player = new();
    private readonly FakePttSessionBridge _bridge = new();
    private readonly TurnManager _turnManager;
    private readonly TalkModeController _controller;

    public TalkModeControllerTests()
    {
        _bridge.AddSession("test-session");
        _turnManager = new TurnManager(_mic);
        _controller = new TalkModeController(
            _voiceSession, _mic, _player, _bridge, _turnManager);
    }

    public void Dispose()
    {
        _controller.Dispose();
        _mic.Dispose();
        _player.Dispose();
    }

    // ---------------------------------------------------------------
    // [CONSTRUCTOR] Null-argument validation
    // ---------------------------------------------------------------

    [Fact]
    public void Ctor_NullVoiceSession_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TalkModeController(null!, _mic, _player, _bridge, _turnManager));
    }

    [Fact]
    public void Ctor_NullMicCapture_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TalkModeController(_voiceSession, null!, _player, _bridge, _turnManager));
    }

    [Fact]
    public void Ctor_NullAudioPlayer_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TalkModeController(_voiceSession, _mic, null!, _bridge, _turnManager));
    }

    [Fact]
    public void Ctor_NullSessionBridge_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TalkModeController(_voiceSession, _mic, _player, null!, _turnManager));
    }

    [Fact]
    public void Ctor_NullTurnManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TalkModeController(_voiceSession, _mic, _player, _bridge, null!));
    }

    // ---------------------------------------------------------------
    // [INITIAL STATE]
    // ---------------------------------------------------------------

    [Fact]
    public void InitialState_IsOff()
    {
        Assert.Equal(TalkModeState.Off, _controller.State);
    }

    [Fact]
    public void InitialState_IsNotActive()
    {
        Assert.False(_controller.IsActive);
    }

    // ---------------------------------------------------------------
    // AC1: Activate Talk Mode — Off → Listening
    // ---------------------------------------------------------------

    [Fact]
    public async Task ActivateAsync_TransitionsToListening()
    {
        await _controller.ActivateAsync();

        Assert.Equal(TalkModeState.Listening, _controller.State);
    }

    [Fact]
    public async Task ActivateAsync_IsActiveReturnsTrue()
    {
        await _controller.ActivateAsync();

        Assert.True(_controller.IsActive);
    }

    [Fact]
    public async Task ActivateAsync_StartsMicCapture()
    {
        await _controller.ActivateAsync();

        Assert.True(_mic.IsCapturing);
    }

    [Fact]
    public async Task ActivateAsync_FiresStateChangedToListening()
    {
        var states = new List<TalkModeState>();
        _controller.StateChanged += s => states.Add(s);

        await _controller.ActivateAsync();

        Assert.Single(states);
        Assert.Equal(TalkModeState.Listening, states[0]);
    }

    [Fact]
    public async Task ActivateAsync_WhenAlreadyActive_IsNoOp()
    {
        await _controller.ActivateAsync();
        var states = new List<TalkModeState>();
        _controller.StateChanged += s => states.Add(s);

        await _controller.ActivateAsync(); // second call

        Assert.Empty(states); // no additional state changes
        Assert.Equal(TalkModeState.Listening, _controller.State);
    }

    [Fact]
    public async Task ActivateAsync_MicStartFails_StaysOff()
    {
        _mic.ThrowOnStart = true;

        await _controller.ActivateAsync();

        Assert.Equal(TalkModeState.Off, _controller.State);
        Assert.False(_controller.IsActive);
    }

    // ---------------------------------------------------------------
    // AC2: Continuous listening — audio streamed to Voice Live API
    // ---------------------------------------------------------------

    [Fact]
    public async Task Listening_AudioCaptured_StreamedToVoiceSession()
    {
        await _controller.ActivateAsync();

        var audioData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        _mic.SimulateAudioCaptured(audioData);

        await Task.Delay(50); // allow async streaming

        Assert.Single(_voiceSession.SentAudioChunks);
        Assert.Equal(audioData, _voiceSession.SentAudioChunks[0]);
    }

    [Fact]
    public async Task Listening_MultipleAudioChunks_AllStreamedToVoiceSession()
    {
        await _controller.ActivateAsync();

        var chunk1 = new byte[] { 0x01 };
        var chunk2 = new byte[] { 0x02 };
        var chunk3 = new byte[] { 0x03 };
        _mic.SimulateAudioCaptured(chunk1);
        _mic.SimulateAudioCaptured(chunk2);
        _mic.SimulateAudioCaptured(chunk3);

        await Task.Delay(50);

        Assert.Equal(3, _voiceSession.SentAudioChunks.Count);
    }

    // ---------------------------------------------------------------
    // Listening → Processing: Voice API starts response (AudioReceived)
    // ---------------------------------------------------------------

    [Fact]
    public async Task AudioReceived_FromListening_TransitionsToProcessing()
    {
        await _controller.ActivateAsync();

        _voiceSession.SimulateAudioReceived(new byte[] { 0xAA });

        Assert.Equal(TalkModeState.Processing, _controller.State);
    }

    [Fact]
    public async Task AudioReceived_FromListening_FiresStateChanged()
    {
        await _controller.ActivateAsync();
        var states = new List<TalkModeState>();
        _controller.StateChanged += s => states.Add(s);

        _voiceSession.SimulateAudioReceived(new byte[] { 0xAA });

        Assert.Contains(TalkModeState.Processing, states);
    }

    [Fact]
    public async Task AudioReceived_FromListening_MutesMic()
    {
        await _controller.ActivateAsync();
        Assert.True(_mic.IsCapturing);

        _voiceSession.SimulateAudioReceived(new byte[] { 0xAA });

        // TurnManager mutes mic asynchronously via Task.Run
        await Task.Delay(100);

        Assert.False(_mic.IsCapturing);
    }

    [Fact]
    public async Task AudioReceived_BuffersAudioDuringProcessing()
    {
        await _controller.ActivateAsync();

        var chunk1 = new byte[] { 0x01, 0x02 };
        var chunk2 = new byte[] { 0x03, 0x04 };

        _voiceSession.SimulateAudioReceived(chunk1); // Listening → Processing + buffer
        _voiceSession.SimulateAudioReceived(chunk2); // still Processing, buffer more

        // Trigger playback to verify buffer contents
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);

        Assert.True(_player.PlayWasCalled);
        Assert.Equal(4, _player.LastPlayedAudio!.Length);
    }

    // ---------------------------------------------------------------
    // Processing → Speaking: ResponseDone fires, play buffered audio
    // ---------------------------------------------------------------

    [Fact]
    public async Task ResponseDone_FromProcessing_TransitionsToSpeaking()
    {
        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        Assert.Equal(TalkModeState.Processing, _controller.State);

        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);

        Assert.Equal(TalkModeState.Speaking, _controller.State);
    }

    [Fact]
    public async Task ResponseDone_PlaysBufferedAudio()
    {
        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01, 0x02 });
        _voiceSession.SimulateAudioReceived(new byte[] { 0x03, 0x04 });

        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);

        Assert.True(_player.PlayWasCalled);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, _player.LastPlayedAudio);
    }

    [Fact]
    public async Task ResponseDone_NoAudio_ResumesListening()
    {
        // The QA edge-case tests cover this path more thoroughly.
        // See TalkModeEdgeCaseTests.ResponseDone_AfterBufferOverflow_ResumesListening
        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        Assert.Equal(TalkModeState.Processing, _controller.State);
    }

    // ---------------------------------------------------------------
    // AC5: Speaking → Listening: Playback completes, resume after delay
    // ---------------------------------------------------------------

    [Fact]
    public async Task PlaybackCompleted_FromSpeaking_TransitionsToListening()
    {
        // Full cycle to Speaking
        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);
        Assert.Equal(TalkModeState.Speaking, _controller.State);

        // Simulate playback completed
        _player.SimulatePlaybackCompleted();

        Assert.Equal(TalkModeState.Listening, _controller.State);
    }

    [Fact]
    public async Task PlaybackCompleted_UnmutesMicAfterDelay()
    {
        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        await Task.Delay(100); // wait for mic mute
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);
        Assert.Equal(TalkModeState.Speaking, _controller.State);
        Assert.False(_mic.IsCapturing); // mic should be muted

        _player.SimulatePlaybackCompleted();

        // State should be Listening immediately
        Assert.Equal(TalkModeState.Listening, _controller.State);

        // But mic unmute happens after 500ms delay
        Assert.False(_mic.IsCapturing);
        await Task.Delay(600); // wait for post-playback delay
        Assert.True(_mic.IsCapturing);
    }

    // ---------------------------------------------------------------
    // Full conversation loop: Off→Listening→Processing→Speaking→Listening
    // ---------------------------------------------------------------

    [Fact]
    public async Task FullConversationLoop()
    {
        var states = new List<TalkModeState>();
        _controller.StateChanged += s => states.Add(s);

        // 1. Activate → Listening
        await _controller.ActivateAsync();
        Assert.Equal(TalkModeState.Listening, _controller.State);

        // 2. Audio response arrives → Processing
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01, 0x02 });
        Assert.Equal(TalkModeState.Processing, _controller.State);

        // 3. More audio + ResponseDone → Speaking
        _voiceSession.SimulateAudioReceived(new byte[] { 0x03, 0x04 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);
        Assert.Equal(TalkModeState.Speaking, _controller.State);

        // 4. Playback done → Listening (loop back)
        _player.SimulatePlaybackCompleted();
        Assert.Equal(TalkModeState.Listening, _controller.State);

        Assert.Equal(
            new[]
            {
                TalkModeState.Listening,   // Activate
                TalkModeState.Processing,  // Audio received
                TalkModeState.Speaking,    // ResponseDone
                TalkModeState.Listening    // Playback done
            },
            states);
    }

    [Fact]
    public async Task MultipleConversationTurns()
    {
        await _controller.ActivateAsync();

        // Turn 1
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);
        _player.SimulatePlaybackCompleted();
        await Task.Delay(600); // wait for mic unmute
        Assert.Equal(TalkModeState.Listening, _controller.State);
        Assert.True(_mic.IsCapturing);

        // Turn 2
        _voiceSession.SimulateAudioReceived(new byte[] { 0x02 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);
        _player.SimulatePlaybackCompleted();

        Assert.Equal(TalkModeState.Listening, _controller.State);
    }

    // ---------------------------------------------------------------
    // AC6/AC7: Deactivate Talk Mode
    // ---------------------------------------------------------------

    [Fact]
    public async Task DeactivateAsync_FromListening_TransitionsToOff()
    {
        await _controller.ActivateAsync();
        Assert.Equal(TalkModeState.Listening, _controller.State);

        await _controller.DeactivateAsync();

        Assert.Equal(TalkModeState.Off, _controller.State);
        Assert.False(_controller.IsActive);
    }

    [Fact]
    public async Task DeactivateAsync_FromListening_StopsMic()
    {
        await _controller.ActivateAsync();

        await _controller.DeactivateAsync();

        Assert.False(_mic.IsCapturing);
    }

    [Fact]
    public async Task DeactivateAsync_FromProcessing_TransitionsToOff()
    {
        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        Assert.Equal(TalkModeState.Processing, _controller.State);

        await _controller.DeactivateAsync();

        Assert.Equal(TalkModeState.Off, _controller.State);
    }

    [Fact]
    public async Task DeactivateAsync_FromSpeaking_TransitionsToOff()
    {
        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);
        Assert.Equal(TalkModeState.Speaking, _controller.State);

        await _controller.DeactivateAsync();

        Assert.Equal(TalkModeState.Off, _controller.State);
    }

    [Fact]
    public async Task DeactivateAsync_FromSpeaking_StopsPlayback()
    {
        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);

        await _controller.DeactivateAsync();

        Assert.True(_player.StopWasCalled);
    }

    [Fact]
    public async Task DeactivateAsync_FromOff_IsNoOp()
    {
        var states = new List<TalkModeState>();
        _controller.StateChanged += s => states.Add(s);

        await _controller.DeactivateAsync();

        Assert.Empty(states);
        Assert.Equal(TalkModeState.Off, _controller.State);
    }

    [Fact]
    public async Task DeactivateAsync_FiresStateChangedToOff()
    {
        await _controller.ActivateAsync();
        var states = new List<TalkModeState>();
        _controller.StateChanged += s => states.Add(s);

        await _controller.DeactivateAsync();

        Assert.Contains(TalkModeState.Off, states);
    }

    [Fact]
    public async Task DeactivateAsync_ClearsAudioBuffer()
    {
        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01, 0x02 });

        await _controller.DeactivateAsync();

        // Reactivate and check no stale audio is played
        await _controller.ActivateAsync();
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);

        // Should not have played stale audio
        Assert.False(_player.PlayWasCalled);
    }

    // ---------------------------------------------------------------
    // Error recovery: → Listening (not Off)
    // ---------------------------------------------------------------

    [Fact]
    public async Task VoiceError_DuringProcessing_ReturnsToListening()
    {
        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        Assert.Equal(TalkModeState.Processing, _controller.State);

        _voiceSession.SimulateError("API error");

        Assert.Equal(TalkModeState.Listening, _controller.State);
        Assert.True(_controller.IsActive); // stays active!
    }

    [Fact]
    public async Task VoiceError_DuringSpeaking_ReturnsToListening()
    {
        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);
        Assert.Equal(TalkModeState.Speaking, _controller.State);

        _voiceSession.SimulateError("API error");

        Assert.Equal(TalkModeState.Listening, _controller.State);
        Assert.True(_controller.IsActive);
    }

    [Fact]
    public async Task VoiceError_DuringListening_StaysListening()
    {
        await _controller.ActivateAsync();
        Assert.Equal(TalkModeState.Listening, _controller.State);

        _voiceSession.SimulateError("API error");

        Assert.Equal(TalkModeState.Listening, _controller.State);
        Assert.True(_controller.IsActive);
    }

    [Fact]
    public async Task VoiceError_WhenOff_IsIgnored()
    {
        var states = new List<TalkModeState>();
        _controller.StateChanged += s => states.Add(s);

        _voiceSession.SimulateError("API error");

        Assert.Empty(states);
        Assert.Equal(TalkModeState.Off, _controller.State);
    }

    // ---------------------------------------------------------------
    // Disconnect → deactivate Talk Mode
    // ---------------------------------------------------------------

    [Fact]
    public async Task VoiceDisconnected_DeactivatesTalkMode()
    {
        await _controller.ActivateAsync();

        _voiceSession.SimulateDisconnected();
        await Task.Delay(100); // async deactivation via Task.Run

        Assert.Equal(TalkModeState.Off, _controller.State);
        Assert.False(_controller.IsActive);
    }

    [Fact]
    public async Task VoiceDisconnected_FromProcessing_DeactivatesCleanly()
    {
        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        Assert.Equal(TalkModeState.Processing, _controller.State);

        _voiceSession.SimulateDisconnected();
        await Task.Delay(100);

        Assert.Equal(TalkModeState.Off, _controller.State);
    }

    [Fact]
    public async Task VoiceDisconnected_FromSpeaking_DeactivatesCleanly()
    {
        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);
        Assert.Equal(TalkModeState.Speaking, _controller.State);

        _voiceSession.SimulateDisconnected();
        await Task.Delay(100);

        Assert.Equal(TalkModeState.Off, _controller.State);
    }

    // ---------------------------------------------------------------
    // AC4: Mic muted during playback (echo prevention)
    // ---------------------------------------------------------------

    [Fact]
    public async Task MicMuted_DuringProcessingAndSpeaking()
    {
        await _controller.ActivateAsync();
        Assert.True(_mic.IsCapturing);

        // Audio received → Processing, mic should be muted
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        await Task.Delay(100); // wait for async mute
        Assert.False(_mic.IsCapturing);

        // ResponseDone → Speaking, mic still muted
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);
        Assert.Equal(TalkModeState.Speaking, _controller.State);
        Assert.False(_mic.IsCapturing);
    }

    // ---------------------------------------------------------------
    // Audio not streamed when not Listening
    // ---------------------------------------------------------------

    [Fact]
    public async Task AudioCaptured_WhenNotListening_NotSentToVoiceSession()
    {
        // Don't activate — state is Off
        _mic.SimulateAudioCaptured(new byte[] { 0x01 });
        await Task.Delay(50);

        Assert.Empty(_voiceSession.SentAudioChunks);
    }

    [Fact]
    public async Task AudioCaptured_DuringProcessing_NotSentToVoiceSession()
    {
        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        Assert.Equal(TalkModeState.Processing, _controller.State);

        _voiceSession.SentAudioChunks.Clear();
        _mic.SimulateAudioCaptured(new byte[] { 0x02 });
        await Task.Delay(50);

        Assert.Empty(_voiceSession.SentAudioChunks);
    }

    // ---------------------------------------------------------------
    // StateChanged event fires for every transition
    // ---------------------------------------------------------------

    [Fact]
    public async Task StateChanged_FiresForEveryTransition()
    {
        var states = new List<TalkModeState>();
        _controller.StateChanged += s => states.Add(s);

        await _controller.ActivateAsync();                         // Off → Listening
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 }); // Listening → Processing
        _voiceSession.SimulateResponseDone();                     // Processing → Speaking
        await Task.Delay(100);
        _player.SimulatePlaybackCompleted();                      // Speaking → Listening

        Assert.Equal(4, states.Count);
        Assert.Equal(TalkModeState.Listening, states[0]);
        Assert.Equal(TalkModeState.Processing, states[1]);
        Assert.Equal(TalkModeState.Speaking, states[2]);
        Assert.Equal(TalkModeState.Listening, states[3]);
    }

    // ---------------------------------------------------------------
    // Reactivation after deactivation
    // ---------------------------------------------------------------

    [Fact]
    public async Task CanReactivate_AfterDeactivation()
    {
        await _controller.ActivateAsync();
        await _controller.DeactivateAsync();
        Assert.Equal(TalkModeState.Off, _controller.State);

        await _controller.ActivateAsync();
        Assert.Equal(TalkModeState.Listening, _controller.State);
        Assert.True(_controller.IsActive);
    }

    // ---------------------------------------------------------------
    // Audio buffer overflow protection
    // ---------------------------------------------------------------

    [Fact]
    public async Task AudioBuffer_ExceedsLimit_Discards()
    {
        await _controller.ActivateAsync();

        // First chunk triggers Listening → Processing
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        Assert.Equal(TalkModeState.Processing, _controller.State);

        // Flood with large chunks to exceed 10MB limit
        var largeChunk = new byte[5 * 1024 * 1024]; // 5MB
        _voiceSession.SimulateAudioReceived(largeChunk);
        _voiceSession.SimulateAudioReceived(largeChunk); // would exceed 10MB
        _voiceSession.SimulateAudioReceived(largeChunk); // exceeds, should discard

        // Should still be in Processing state (not crash)
        Assert.Equal(TalkModeState.Processing, _controller.State);
    }

    // ---------------------------------------------------------------
    // Playback error recovery
    // ---------------------------------------------------------------

    [Fact]
    public async Task PlaybackError_ResumesListening()
    {
        _player.ThrowOnPlay = true;

        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(200);

        // After playback error, should resume Listening
        Assert.Equal(TalkModeState.Listening, _controller.State);
        Assert.True(_controller.IsActive);
    }

    // ---------------------------------------------------------------
    // CLI disconnect during Talk Mode
    // ---------------------------------------------------------------

    [Fact]
    public async Task CliDisconnect_DuringTalkMode_StaysActive()
    {
        await _controller.ActivateAsync();

        // Remove CLI session — Talk Mode should continue voice-only
        _bridge.RemoveSession("test-session");

        Assert.Equal(TalkModeState.Listening, _controller.State);
        Assert.True(_controller.IsActive);
    }

    // ---------------------------------------------------------------
    // Dispose safety
    // ---------------------------------------------------------------

    [Fact]
    public async Task Dispose_WhileActive_CleanedUp()
    {
        await _controller.ActivateAsync();

        // Should not throw
        _controller.Dispose();

        // Events should be unwired — subsequent events should not crash
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        _player.SimulatePlaybackCompleted();
    }
}
