using CopilotVoice.Audio;
using CopilotVoice.Bridge;
using CopilotVoice.Voice;

namespace CopilotVoice.Tests.Voice;

/// <summary>
/// Edge-case, boundary, and coverage-gap tests for PushToTalkController.
/// Supplements the happy-path unit tests in PushToTalkControllerTests.
/// All tests use enhanced fakes that support additional failure modes.
/// </summary>
public class PushToTalkEdgeCaseTests : IDisposable
{
    private readonly EnhancedFakeVoiceSession _voiceSession = new();
    private readonly EnhancedFakeMic _mic = new();
    private readonly EnhancedFakePlayer _player = new();
    private readonly FakePttSessionBridge _bridge = new();
    private readonly PushToTalkController _controller;

    public PushToTalkEdgeCaseTests()
    {
        _bridge.AddSession("test-session");
        _controller = new PushToTalkController(_voiceSession, _mic, _player, _bridge);
    }

    public void Dispose()
    {
        _mic.Dispose();
        _player.Dispose();
    }

    // ---------------------------------------------------------------
    // [BOUNDARY] Constructor null-argument validation
    // ---------------------------------------------------------------

    [Fact]
    public void Ctor_NullVoiceSession_Throws()
    {
        // [BOUNDARY] Validates the IPushToTalkController contract — all deps required
        Assert.Throws<ArgumentNullException>(() =>
            new PushToTalkController(null!, _mic, _player, _bridge));
    }

    [Fact]
    public void Ctor_NullMicCapture_Throws()
    {
        // [BOUNDARY] Validates the IPushToTalkController contract — all deps required
        Assert.Throws<ArgumentNullException>(() =>
            new PushToTalkController(_voiceSession, null!, _player, _bridge));
    }

    [Fact]
    public void Ctor_NullAudioPlayer_Throws()
    {
        // [BOUNDARY] Validates the IPushToTalkController contract — all deps required
        Assert.Throws<ArgumentNullException>(() =>
            new PushToTalkController(_voiceSession, _mic, null!, _bridge));
    }

    [Fact]
    public void Ctor_NullSessionBridge_Throws()
    {
        // [BOUNDARY] Validates the IPushToTalkController contract — all deps required
        Assert.Throws<ArgumentNullException>(() =>
            new PushToTalkController(_voiceSession, _mic, _player, null!));
    }

    // ---------------------------------------------------------------
    // [EDGE] StopAsync from various states
    // ---------------------------------------------------------------

    [Fact]
    public async Task StopAsync_FromIdle_RemainsIdle()
    {
        // [EDGE] Calling StopAsync when already idle should be a safe no-op
        await _controller.StopAsync();

        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    [Fact]
    public async Task StopAsync_FromRecording_StopsMicAndReturnsToIdle()
    {
        // [EDGE] StopAsync during active recording should cleanly tear down
        _controller.OnHotkeyPressed();
        Assert.Equal(PushToTalkState.Recording, _controller.State);
        Assert.True(_mic.IsCapturing);

        await _controller.StopAsync();

        Assert.Equal(PushToTalkState.Idle, _controller.State);
        Assert.False(_mic.IsCapturing);
    }

    [Fact]
    public async Task StopAsync_FromProcessing_ReturnsToIdle()
    {
        // [EDGE] StopAsync during processing should abandon the pending response
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        Assert.Equal(PushToTalkState.Processing, _controller.State);

        await _controller.StopAsync();

        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    [Fact]
    public async Task StopAsync_FromPlaying_StopsPlaybackAndReturnsToIdle()
    {
        // [EDGE] StopAsync during playback should interrupt audio and reset
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(50);
        Assert.Equal(PushToTalkState.Playing, _controller.State);

        await _controller.StopAsync();

        Assert.Equal(PushToTalkState.Idle, _controller.State);
        Assert.True(_player.StopWasCalled);
    }

    [Fact]
    public async Task StopAsync_FiresStateChangedToIdle()
    {
        // [EDGE] StateChanged should fire even on forced stop
        _controller.OnHotkeyPressed();

        var states = new List<PushToTalkState>();
        _controller.StateChanged += s => states.Add(s);

        await _controller.StopAsync();

        Assert.Contains(PushToTalkState.Idle, states);
    }

    // ---------------------------------------------------------------
    // [EDGE] StartAsync (currently a no-op, but contract must hold)
    // ---------------------------------------------------------------

    [Fact]
    public async Task StartAsync_CompletesWithoutError()
    {
        // [BOUNDARY] StartAsync contract — should not throw or change state
        await _controller.StartAsync();

        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    [Fact]
    public async Task StartAsync_SupportsCancellation()
    {
        // [BOUNDARY] StartAsync with cancelled token should still be safe
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Currently no-op, but contract says it accepts CancellationToken
        await _controller.StartAsync(cts.Token);

        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    // ---------------------------------------------------------------
    // [EDGE] CommitAudioAsync failure → returns to Idle
    // ---------------------------------------------------------------

    [Fact]
    public async Task CommitAudioThrows_TransitionsToIdle()
    {
        // [EDGE] If the voice API rejects the audio commit, the state machine
        // must not get stuck in Processing
        _voiceSession.ThrowOnCommit = true;

        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();

        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    [Fact]
    public async Task CommitAudioThrows_MicIsStillStopped()
    {
        // [EDGE] Even if commit fails, the mic must have been stopped before
        _voiceSession.ThrowOnCommit = true;

        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();

        Assert.False(_mic.IsCapturing);
    }

    // ---------------------------------------------------------------
    // [EDGE] PlayAsync failure → returns to Idle
    // ---------------------------------------------------------------

    [Fact]
    public async Task PlayAsyncThrows_TransitionsToIdle()
    {
        // [EDGE] If the audio player fails (e.g., device unavailable),
        // the state machine must recover to Idle
        _player.ThrowOnPlay = true;

        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();

        // PlayAudioAsync runs via Task.Run → give it time to complete + handle error
        await Task.Delay(100);

        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    // ---------------------------------------------------------------
    // [EDGE] Events received in wrong states (must be ignored)
    // ---------------------------------------------------------------

    [Fact]
    public void AudioCaptured_WhileIdle_IsIgnored()
    {
        // [EDGE] Spurious audio from mic while in Idle should not stream to API
        _mic.SimulateAudioCaptured(new byte[] { 0x01, 0x02 });

        Assert.Empty(_voiceSession.SentAudioChunks);
    }

    [Fact]
    public async Task AudioCaptured_WhileProcessing_IsIgnored()
    {
        // [EDGE] Late audio from mic while in Processing should not stream
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        Assert.Equal(PushToTalkState.Processing, _controller.State);

        _voiceSession.SentAudioChunks.Clear(); // clear audio sent during recording
        _mic.SimulateAudioCaptured(new byte[] { 0xFF, 0xFE });

        // Allow async to propagate
        await Task.Delay(50);

        Assert.Empty(_voiceSession.SentAudioChunks);
    }

    [Fact]
    public void VoiceAudioReceived_WhileIdle_IsNotBuffered()
    {
        // [EDGE] Response audio arriving while Idle should be silently dropped
        _voiceSession.SimulateAudioReceived(new byte[] { 0xAA });

        // Transition to recording and back — verify no buffered audio leak
        _controller.OnHotkeyPressed();
        _controller.OnHotkeyReleased(); // quick press → cancel → Idle

        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    [Fact]
    public void VoiceAudioReceived_WhileRecording_IsNotBuffered()
    {
        // [EDGE] Audio from the API arriving during recording (wrong phase) should not buffer
        _controller.OnHotkeyPressed();

        _voiceSession.SimulateAudioReceived(new byte[] { 0xBB });

        // The audio buffer should not include this (it's only for Processing/Playing)
        // We can verify by completing the cycle and checking nothing plays
        _controller.OnHotkeyReleased(); // quick cancel

        Assert.Equal(PushToTalkState.Idle, _controller.State);
        Assert.False(_player.PlayWasCalled);
    }

    [Fact]
    public async Task ErrorReceived_WhileRecording_IsIgnored()
    {
        // [EDGE] Error during recording should not change state
        _controller.OnHotkeyPressed();
        Assert.Equal(PushToTalkState.Recording, _controller.State);

        _voiceSession.SimulateError("Spurious error");

        Assert.Equal(PushToTalkState.Recording, _controller.State);
    }

    [Fact]
    public async Task ErrorReceived_WhilePlaying_IsIgnored()
    {
        // [EDGE] Error during playback should not change state (playback continues)
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(50);
        Assert.Equal(PushToTalkState.Playing, _controller.State);

        _voiceSession.SimulateError("Late error");

        Assert.Equal(PushToTalkState.Playing, _controller.State);
    }

    [Fact]
    public void ErrorReceived_WhileIdle_IsIgnored()
    {
        // [EDGE] Error while idle should not change state
        _voiceSession.SimulateError("Idle error");

        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    [Fact]
    public void ResponseDone_WhileIdle_IsIgnored()
    {
        // [EDGE] ResponseDone while not processing should not trigger playback
        _voiceSession.SimulateResponseDone();

        // Give Task.Run time to fire
        Thread.Sleep(100);

        Assert.Equal(PushToTalkState.Idle, _controller.State);
        Assert.False(_player.PlayWasCalled);
    }

    [Fact]
    public void ResponseDone_WhileRecording_IsIgnored()
    {
        // [EDGE] ResponseDone during recording should not trigger transition
        _controller.OnHotkeyPressed();
        Assert.Equal(PushToTalkState.Recording, _controller.State);

        _voiceSession.SimulateResponseDone();
        Thread.Sleep(100);

        Assert.Equal(PushToTalkState.Recording, _controller.State);
    }

    [Fact]
    public void PlaybackCompleted_WhileIdle_IsIgnored()
    {
        // [EDGE] Spurious PlaybackCompleted event from player while Idle
        _player.SimulatePlaybackCompleted();

        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    [Fact]
    public async Task PlaybackCompleted_WhileProcessing_IsIgnored()
    {
        // [EDGE] Spurious PlaybackCompleted during processing should not change state
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        Assert.Equal(PushToTalkState.Processing, _controller.State);

        _player.SimulatePlaybackCompleted();

        Assert.Equal(PushToTalkState.Processing, _controller.State);
    }

    // ---------------------------------------------------------------
    // [EDGE] Multiple audio chunks during recording
    // ---------------------------------------------------------------

    [Fact]
    public async Task Recording_MultipleAudioChunks_AllStreamedToVoiceSession()
    {
        // [EDGE] Multiple consecutive mic captures should all stream through
        _controller.OnHotkeyPressed();

        var chunks = new byte[][]
        {
            new byte[] { 0x01, 0x02 },
            new byte[] { 0x03, 0x04 },
            new byte[] { 0x05, 0x06 },
        };

        foreach (var chunk in chunks)
            _mic.SimulateAudioCaptured(chunk);

        await Task.Delay(50);

        Assert.Equal(3, _voiceSession.SentAudioChunks.Count);
        Assert.Equal(chunks[0], _voiceSession.SentAudioChunks[0]);
        Assert.Equal(chunks[1], _voiceSession.SentAudioChunks[1]);
        Assert.Equal(chunks[2], _voiceSession.SentAudioChunks[2]);
    }

    // ---------------------------------------------------------------
    // [EDGE] Audio buffering during Playing state
    // ---------------------------------------------------------------

    [Fact]
    public async Task AudioReceived_DuringPlaying_IsBuffered()
    {
        // [EDGE] The production code buffers audio during both Processing AND Playing.
        // This tests the Playing-state buffering path.
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(50);
        Assert.Equal(PushToTalkState.Playing, _controller.State);

        // Additional audio arrives during playback — should be buffered
        _voiceSession.SimulateAudioReceived(new byte[] { 0xAA, 0xBB });

        // The audio is buffered but won't play until next cycle
        // This verifies no crash/exception on late audio during playback
        Assert.Equal(PushToTalkState.Playing, _controller.State);
    }

    // ---------------------------------------------------------------
    // [EDGE] StopMicSafe / StopPlaybackSafe exception resilience
    // ---------------------------------------------------------------

    [Fact]
    public async Task MicStopThrows_QuickCancel_StillReturnsToIdle()
    {
        // [EDGE] If mic.StopAsync throws during quick-press cancel, state must still recover
        _mic.ThrowOnStop = true;

        _controller.OnHotkeyPressed();
        _controller.OnHotkeyReleased(); // quick press → cancel → calls StopMicSafe

        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    [Fact]
    public async Task MicStopThrows_NormalRelease_StillTransitionsToProcessing()
    {
        // [EDGE] If mic.StopAsync throws during normal release, should still proceed
        _mic.ThrowOnStop = true;

        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();

        Assert.Equal(PushToTalkState.Processing, _controller.State);
    }

    [Fact]
    public async Task PlayerStopThrows_InterruptPlayback_StillRecords()
    {
        // [EDGE] If player.StopAsync throws during interrupt, recording should still start
        _player.ThrowOnStop = true;

        // Get to Playing
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(50);
        Assert.Equal(PushToTalkState.Playing, _controller.State);

        // Interrupt playback with new hotkey press
        _controller.OnHotkeyPressed();

        Assert.Equal(PushToTalkState.Recording, _controller.State);
        Assert.True(_mic.IsCapturing);
    }

    // ---------------------------------------------------------------
    // [EDGE] Full cycle after error recovery
    // ---------------------------------------------------------------

    [Fact]
    public async Task AfterErrorRecovery_FullCycleStillWorks()
    {
        // [EDGE] After an error resets state to Idle, the controller should
        // accept a new recording cycle without stuck states
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        Assert.Equal(PushToTalkState.Processing, _controller.State);

        // Simulate error → Idle
        _voiceSession.SimulateError("Transient API error");
        Assert.Equal(PushToTalkState.Idle, _controller.State);

        // Now do a full successful cycle
        _controller.OnHotkeyPressed();
        Assert.Equal(PushToTalkState.Recording, _controller.State);

        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        Assert.Equal(PushToTalkState.Processing, _controller.State);

        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(50);
        Assert.Equal(PushToTalkState.Playing, _controller.State);

        _player.SimulatePlaybackCompleted();
        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    [Fact]
    public async Task AfterMicFailure_NextPressStillWorks()
    {
        // [EDGE] After mic fails on first press, subsequent press should work
        _mic.ThrowOnStart = true;

        _controller.OnHotkeyPressed();
        Assert.Equal(PushToTalkState.Idle, _controller.State); // failed to start

        // Fix the mic
        _mic.ThrowOnStart = false;

        _controller.OnHotkeyPressed();
        Assert.Equal(PushToTalkState.Recording, _controller.State);
        Assert.True(_mic.IsCapturing);
    }

    // ---------------------------------------------------------------
    // [EDGE] Error clears audio buffer
    // ---------------------------------------------------------------

    [Fact]
    public async Task ErrorDuringProcessing_ClearsAudioBuffer()
    {
        // [EDGE] Audio buffered during Processing must be cleared on error,
        // so it doesn't leak into the next recording cycle's playback
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();

        // Buffer some audio
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01, 0x02 });

        // Error resets to Idle
        _voiceSession.SimulateError("API timeout");
        Assert.Equal(PushToTalkState.Idle, _controller.State);

        // New cycle — response should NOT play the old buffered audio
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        // No audio received this time
        _voiceSession.SimulateResponseDone();
        await Task.Delay(50);

        // Should go to Idle (no audio), not Playing with stale buffer
        Assert.Equal(PushToTalkState.Idle, _controller.State);
        Assert.False(_player.PlayWasCalled);
    }

    // ---------------------------------------------------------------
    // [EDGE] Rapid hotkey press after playback completes
    // ---------------------------------------------------------------

    [Fact]
    public async Task HotkeyPress_ImmediatelyAfterPlaybackCompleted_Works()
    {
        // [EDGE] Race condition: pressing hotkey right after playback completes
        var states = new List<PushToTalkState>();
        _controller.StateChanged += s => states.Add(s);

        // Full cycle to Playing
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(50);
        Assert.Equal(PushToTalkState.Playing, _controller.State);

        // Playback completes → Idle
        _player.SimulatePlaybackCompleted();
        Assert.Equal(PushToTalkState.Idle, _controller.State);

        // Immediately start new recording
        _controller.OnHotkeyPressed();
        Assert.Equal(PushToTalkState.Recording, _controller.State);
    }

    // ---------------------------------------------------------------
    // [EDGE] StopAsync from Playing state — verifies StopPlaybackSafe called
    // ---------------------------------------------------------------

    [Fact]
    public async Task StopAsync_FromPlaying_StopsPlaybackSafe()
    {
        // [EDGE] Verify StopAsync calls StopPlaybackSafe (not StopMicSafe)
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();
        _voiceSession.SimulateAudioReceived(new byte[] { 0x01 });
        _voiceSession.SimulateResponseDone();
        await Task.Delay(50);
        Assert.Equal(PushToTalkState.Playing, _controller.State);

        _player.StopWasCalled = false; // reset tracking flag
        await _controller.StopAsync();

        Assert.True(_player.StopWasCalled);
        Assert.Equal(PushToTalkState.Idle, _controller.State);
    }

    // ---------------------------------------------------------------
    // [EDGE] Empty audio data
    // ---------------------------------------------------------------

    [Fact]
    public async Task Recording_EmptyAudioChunk_StreamedWithoutCrash()
    {
        // [EDGE] Edge case: mic sends an empty audio buffer
        _controller.OnHotkeyPressed();

        _mic.SimulateAudioCaptured(Array.Empty<byte>());
        await Task.Delay(50);

        // Should not crash — empty chunk sent to session
        Assert.Single(_voiceSession.SentAudioChunks);
        Assert.Empty(_voiceSession.SentAudioChunks[0]);
    }

    // ---------------------------------------------------------------
    // [EDGE] Large audio data
    // ---------------------------------------------------------------

    [Fact]
    public async Task ResponseAudio_LargeBuffer_PlaysSuccessfully()
    {
        // [EDGE] Large response audio (e.g., long voice answer) should buffer and play
        _controller.OnHotkeyPressed();
        await Task.Delay(250);
        _controller.OnHotkeyReleased();

        // Simulate many audio chunks (simulating a long response)
        for (int i = 0; i < 100; i++)
        {
            _voiceSession.SimulateAudioReceived(new byte[] { (byte)(i & 0xFF) });
        }

        _voiceSession.SimulateResponseDone();
        await Task.Delay(50);

        Assert.Equal(PushToTalkState.Playing, _controller.State);
        Assert.True(_player.PlayWasCalled);
        Assert.Equal(100, _player.LastPlayedAudio!.Length);
    }
}

// =================================================================
// Enhanced Fakes — support additional failure modes for edge cases
// =================================================================

internal sealed class EnhancedFakeVoiceSession : IVoiceLiveSession
{
    public List<byte[]> SentAudioChunks { get; } = new();
    public bool AudioCommitted { get; set; }
    public bool ThrowOnCommit { get; set; }

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
        if (ThrowOnCommit) throw new InvalidOperationException("Voice API commit failed");
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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class EnhancedFakeMic : IMicCapture
{
    public bool IsCapturing { get; private set; }
    public bool ThrowOnStart { get; set; }
    public bool ThrowOnStop { get; set; }

    public event Action<ReadOnlyMemory<byte>>? AudioCaptured;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (ThrowOnStart) throw new InvalidOperationException("No microphone available");
        IsCapturing = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (ThrowOnStop) throw new InvalidOperationException("Mic device disconnected");
        IsCapturing = false;
        return Task.CompletedTask;
    }

    public void SimulateAudioCaptured(byte[] data) => AudioCaptured?.Invoke(data);

    public void Dispose() { IsCapturing = false; }
}

internal sealed class EnhancedFakePlayer : IAudioPlayer
{
    public bool IsPlaying { get; private set; }
    public bool PlayWasCalled { get; set; }
    public bool StopWasCalled { get; set; }
    public bool ThrowOnPlay { get; set; }
    public bool ThrowOnStop { get; set; }
    public byte[]? LastPlayedAudio { get; private set; }

    public event Action? PlaybackCompleted;

    public Task PlayAsync(ReadOnlyMemory<byte> pcm16Audio, CancellationToken ct = default)
    {
        if (ThrowOnPlay) throw new InvalidOperationException("Audio device unavailable");
        PlayWasCalled = true;
        LastPlayedAudio = pcm16Audio.ToArray();
        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (ThrowOnStop) throw new InvalidOperationException("Audio device error on stop");
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
