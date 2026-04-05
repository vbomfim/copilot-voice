using CopilotVoice.Audio;
using CopilotVoice.Bridge;
using CopilotVoice.Voice;

namespace CopilotVoice.Tests.Voice;

/// <summary>
/// QA Guardian — edge case and boundary tests for TalkModeController.
/// These tests cover paths not exercised by the Developer Guardian's unit tests:
/// - Buffer overflow → ResponseDone path
/// - AudioReceived/PlaybackCompleted/ResponseDone in unexpected states
/// - Empty audio chunks
/// - Error buffer clearing
/// - Deactivation mic cleanup from Processing
/// - Multiple PlaybackCompleted events
/// </summary>
public class TalkModeEdgeCaseTests : IDisposable
{
    private readonly FakeVoiceLiveSession _voiceSession = new();
    private readonly FakeMicCapture _mic = new();
    private readonly FakeAudioPlayer _player = new();
    private readonly FakePttSessionBridge _bridge = new();
    private readonly TurnManager _turnManager;
    private readonly TalkModeController _controller;

    public TalkModeEdgeCaseTests()
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
    // [EDGE] Buffer overflow → ResponseDone should resume Listening
    // Fills the gap left by the incomplete ResponseDone_NoAudio_ResumesListening
    // ---------------------------------------------------------------

    [Fact]
    public async Task ResponseDone_AfterBufferOverflow_ResumesListening()
    {
        // AC5 edge path: buffer overflows → cleared → ResponseDone sees empty buffer → Listening
        await _controller.ActivateAsync();

        // First chunk triggers Listening → Processing and starts buffering
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        Assert.Equal(TalkModeState.Processing, _controller.State);

        // Overflow the 10MB buffer — this clears the buffer
        var largeChunk = new byte[6 * 1024 * 1024]; // 6MB
        _voiceSession.SimulateAudioReceived(largeChunk); // ~6MB total
        _voiceSession.SimulateAudioReceived(largeChunk); // would exceed 10MB → buffer cleared

        // Now ResponseDone fires — buffer is empty → should resume Listening, not Speaking
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);

        Assert.Equal(TalkModeState.Listening, _controller.State);
        Assert.True(_controller.IsActive);
        Assert.False(_player.PlayWasCalled); // no audio played
    }

    // ---------------------------------------------------------------
    // [EDGE] AudioReceived during Speaking state — should be ignored
    // ---------------------------------------------------------------

    [Fact]
    public async Task AudioReceived_DuringSpeaking_IsIgnored()
    {
        await _controller.ActivateAsync();

        // Get to Speaking state
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);
        Assert.Equal(TalkModeState.Speaking, _controller.State);

        var states = new List<TalkModeState>();
        _controller.StateChanged += s => states.Add(s);

        // AudioReceived during Speaking — should NOT transition or buffer
        _voiceSession.SimulateAudioReceived(new byte[] { 0xBB });

        Assert.Equal(TalkModeState.Speaking, _controller.State);
        Assert.Empty(states); // no state change
    }

    // ---------------------------------------------------------------
    // [EDGE] PlaybackCompleted from non-Speaking states — should be no-op
    // ---------------------------------------------------------------

    [Fact]
    public async Task PlaybackCompleted_FromListening_IsNoOp()
    {
        await _controller.ActivateAsync();
        Assert.Equal(TalkModeState.Listening, _controller.State);

        var states = new List<TalkModeState>();
        _controller.StateChanged += s => states.Add(s);

        _player.SimulatePlaybackCompleted();

        Assert.Equal(TalkModeState.Listening, _controller.State);
        Assert.Empty(states);
    }

    [Fact]
    public async Task PlaybackCompleted_FromProcessing_IsNoOp()
    {
        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        Assert.Equal(TalkModeState.Processing, _controller.State);

        var states = new List<TalkModeState>();
        _controller.StateChanged += s => states.Add(s);

        _player.SimulatePlaybackCompleted();

        Assert.Equal(TalkModeState.Processing, _controller.State);
        Assert.Empty(states);
    }

    [Fact]
    public void PlaybackCompleted_WhenOff_IsNoOp()
    {
        Assert.Equal(TalkModeState.Off, _controller.State);

        var states = new List<TalkModeState>();
        _controller.StateChanged += s => states.Add(s);

        _player.SimulatePlaybackCompleted();

        Assert.Equal(TalkModeState.Off, _controller.State);
        Assert.Empty(states);
    }

    // ---------------------------------------------------------------
    // [EDGE] ResponseDone from non-Processing states — should be no-op
    // ---------------------------------------------------------------

    [Fact]
    public async Task ResponseDone_FromListening_IsNoOp()
    {
        await _controller.ActivateAsync();
        Assert.Equal(TalkModeState.Listening, _controller.State);

        var states = new List<TalkModeState>();
        _controller.StateChanged += s => states.Add(s);

        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);

        Assert.Equal(TalkModeState.Listening, _controller.State);
        Assert.Empty(states);
    }

    [Fact]
    public async Task ResponseDone_FromSpeaking_IsNoOp()
    {
        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);
        Assert.Equal(TalkModeState.Speaking, _controller.State);

        var states = new List<TalkModeState>();
        _controller.StateChanged += s => states.Add(s);

        _voiceSession.SimulateResponseDone(); // second ResponseDone
        await Task.Delay(100);

        Assert.Equal(TalkModeState.Speaking, _controller.State);
        Assert.Empty(states);
    }

    [Fact]
    public async Task ResponseDone_WhenOff_IsNoOp()
    {
        Assert.Equal(TalkModeState.Off, _controller.State);

        var states = new List<TalkModeState>();
        _controller.StateChanged += s => states.Add(s);

        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);

        Assert.Equal(TalkModeState.Off, _controller.State);
        Assert.Empty(states);
    }

    // ---------------------------------------------------------------
    // [BOUNDARY] Empty audio chunk handling
    // ---------------------------------------------------------------

    [Fact]
    public async Task EmptyAudioChunk_FromMic_StreamedToVoiceSession()
    {
        await _controller.ActivateAsync();

        _mic.SimulateAudioCaptured(Array.Empty<byte>());
        await Task.Delay(50);

        // Empty chunk should still be streamed (API handles it)
        Assert.Single(_voiceSession.SentAudioChunks);
        Assert.Empty(_voiceSession.SentAudioChunks[0]);
    }

    [Fact]
    public async Task EmptyAudioChunk_FromVoice_TriggersProcessingTransition()
    {
        await _controller.ActivateAsync();

        // Even an empty audio chunk from the API indicates a response is starting
        _voiceSession.SimulateAudioReceived(Array.Empty<byte>());

        Assert.Equal(TalkModeState.Processing, _controller.State);
    }

    // ---------------------------------------------------------------
    // [BOUNDARY] Audio captured during Speaking state
    // ---------------------------------------------------------------

    [Fact]
    public async Task AudioCaptured_DuringSpeaking_NotSentToVoiceSession()
    {
        await _controller.ActivateAsync();

        // Get to Speaking
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);
        Assert.Equal(TalkModeState.Speaking, _controller.State);

        _voiceSession.SentAudioChunks.Clear();
        _mic.SimulateAudioCaptured(new byte[] { 0x02 });
        await Task.Delay(50);

        Assert.Empty(_voiceSession.SentAudioChunks);
    }

    // ---------------------------------------------------------------
    // [EDGE] Error during Processing clears audio buffer
    // ---------------------------------------------------------------

    [Fact]
    public async Task ErrorDuringProcessing_ClearsAudioBuffer_NoStalePlayback()
    {
        await _controller.ActivateAsync();

        // Buffer some audio → Processing
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01, 0x02, 0x03 });
        Assert.Equal(TalkModeState.Processing, _controller.State);

        // Error clears buffer and returns to Listening
        _voiceSession.SimulateError("API error");
        Assert.Equal(TalkModeState.Listening, _controller.State);

        // Now start a new turn — new audio should be fresh, not include stale data
        _voiceSession.SimulateAudioReceived(new byte[] { 0xAA });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);

        Assert.True(_player.PlayWasCalled);
        // Should only contain the new audio, not the stale 0x01,0x02,0x03
        Assert.Single(_player.LastPlayedAudio!); // just 0xAA
        Assert.Equal(0xAA, _player.LastPlayedAudio![0]);
    }

    // ---------------------------------------------------------------
    // [BOUNDARY] Deactivate from Processing stops mic
    // ---------------------------------------------------------------

    [Fact]
    public async Task DeactivateFromProcessing_StopsMic()
    {
        await _controller.ActivateAsync();
        Assert.True(_mic.IsCapturing);

        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        await Task.Delay(100); // wait for async mic mute
        Assert.Equal(TalkModeState.Processing, _controller.State);

        await _controller.DeactivateAsync();

        Assert.False(_mic.IsCapturing);
        Assert.Equal(TalkModeState.Off, _controller.State);
    }

    // ---------------------------------------------------------------
    // [EDGE] Multiple PlaybackCompleted — only first transitions
    // ---------------------------------------------------------------

    [Fact]
    public async Task MultiplePlaybackCompleted_OnlyFirstTransitions()
    {
        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);
        Assert.Equal(TalkModeState.Speaking, _controller.State);

        var states = new List<TalkModeState>();
        _controller.StateChanged += s => states.Add(s);

        // First PlaybackCompleted → Listening
        _player.SimulatePlaybackCompleted();
        Assert.Equal(TalkModeState.Listening, _controller.State);

        // Second PlaybackCompleted — should be no-op (already Listening)
        _player.SimulatePlaybackCompleted();
        Assert.Equal(TalkModeState.Listening, _controller.State);

        // Only one state change should have fired
        Assert.Single(states);
        Assert.Equal(TalkModeState.Listening, states[0]);
    }

    // ---------------------------------------------------------------
    // [EDGE] Rapid activate → deactivate → reactivate
    // ---------------------------------------------------------------

    [Fact]
    public async Task RapidActivateDeactivateReactivate_WorksCleanly()
    {
        await _controller.ActivateAsync();
        Assert.Equal(TalkModeState.Listening, _controller.State);

        await _controller.DeactivateAsync();
        Assert.Equal(TalkModeState.Off, _controller.State);

        await _controller.ActivateAsync();
        Assert.Equal(TalkModeState.Listening, _controller.State);

        // Verify new session works: audio streams correctly
        _mic.SimulateAudioCaptured(new byte[] { 0x01 });
        await Task.Delay(50);
        Assert.NotEmpty(_voiceSession.SentAudioChunks);
    }

    // ---------------------------------------------------------------
    // [EDGE] Error recovery re-enables mic after unmute delay
    // ---------------------------------------------------------------

    [Fact]
    public async Task VoiceError_DuringProcessing_EventuallyRestoredMic()
    {
        await _controller.ActivateAsync();
        Assert.True(_mic.IsCapturing);

        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        await Task.Delay(100); // wait for async mic mute
        Assert.False(_mic.IsCapturing);

        // Error recovers to Listening
        _voiceSession.SimulateError("transient error");
        Assert.Equal(TalkModeState.Listening, _controller.State);

        // Mic should eventually be restored after the unmute delay
        await Task.Delay(600); // 500ms delay + margin
        Assert.True(_mic.IsCapturing);
    }

    // ---------------------------------------------------------------
    // [EDGE] Deactivate during Speaking stops both playback AND mic
    // ---------------------------------------------------------------

    [Fact]
    public async Task DeactivateFromSpeaking_StopsBothPlaybackAndMic()
    {
        await _controller.ActivateAsync();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(100);
        Assert.Equal(TalkModeState.Speaking, _controller.State);

        await _controller.DeactivateAsync();

        Assert.True(_player.StopWasCalled);
        Assert.False(_mic.IsCapturing);
        Assert.Equal(TalkModeState.Off, _controller.State);
    }

    // ---------------------------------------------------------------
    // [EDGE] VoiceDisconnected when already Off — should be safe
    // ---------------------------------------------------------------

    [Fact]
    public async Task VoiceDisconnected_WhenOff_IsNoOp()
    {
        // Controller is Off, simulate disconnect — should not crash
        Assert.Equal(TalkModeState.Off, _controller.State);

        var states = new List<TalkModeState>();
        _controller.StateChanged += s => states.Add(s);

        // Need to wire events first (normally done in ActivateAsync)
        // Since Off, disconnect should be safe even if events fire
        // through other paths
        _voiceSession.SimulateDisconnected();
        await Task.Delay(100);

        Assert.Equal(TalkModeState.Off, _controller.State);
    }

    // ---------------------------------------------------------------
    // [EDGE] AudioReceived when Off — should not transition
    // ---------------------------------------------------------------

    [Fact]
    public void AudioReceived_WhenOff_IsIgnored()
    {
        Assert.Equal(TalkModeState.Off, _controller.State);

        var states = new List<TalkModeState>();
        _controller.StateChanged += s => states.Add(s);

        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });

        Assert.Equal(TalkModeState.Off, _controller.State);
        Assert.Empty(states);
    }
}
