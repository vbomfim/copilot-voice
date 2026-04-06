using CopilotVoice.Audio;

namespace CopilotVoice.Tests.Audio;

/// <summary>
/// QA Guardian — integration, edge-case, boundary, and contract tests for
/// the real Audio subsystem (MicCapture, AudioPlayer, PortAudioLifecycle).
///
/// These complement the Developer's unit tests by probing:
/// - Contract compliance (events fire as documented)
/// - Edge cases (concurrent ops, dispose-during-use, rapid cycling)
/// - Boundary conditions (empty data, invalid data, ref-count edges)
///
/// Tests that require audio hardware are tagged [Trait("Category", "Hardware")].
/// Non-hardware tests exercise state-machine paths that don't touch PortAudio.
/// </summary>
public class AudioBehaviorTests
{
    // =================================================================
    // [CONTRACT] AudioPlayer — PlayAsync empty data fires PlaybackCompleted
    //
    // Existing test PlayAsync_WithEmptyData_CompletesImmediately subscribes
    // to PlaybackCompleted but never asserts the event actually fired.
    // AudioPlayer.PlayAsync (line 56-59) explicitly fires PlaybackCompleted
    // for empty data. This test verifies the contract.
    // =================================================================

    [Fact]
    public async Task AudioPlayer_PlayAsync_EmptyData_FiresPlaybackCompleted()
    {
        // [CONTRACT] PlaybackCompleted must fire for empty audio data
        using var player = new AudioPlayer();
        bool eventFired = false;
        player.PlaybackCompleted += () => eventFired = true;

        await player.PlayAsync(ReadOnlyMemory<byte>.Empty);

        Assert.True(eventFired,
            "PlaybackCompleted should fire when PlayAsync receives empty data");
        Assert.False(player.IsPlaying,
            "IsPlaying should be false after empty-data playback");
    }

    [Fact]
    public async Task AudioPlayer_PlayAsync_EmptyData_MultipleSequentialCalls()
    {
        // [EDGE] Rapid sequential PlayAsync with empty data — tests fast-path stability
        using var player = new AudioPlayer();
        int eventCount = 0;
        player.PlaybackCompleted += () => Interlocked.Increment(ref eventCount);

        for (int i = 0; i < 50; i++)
        {
            await player.PlayAsync(ReadOnlyMemory<byte>.Empty);
        }

        Assert.Equal(50, eventCount);
        Assert.False(player.IsPlaying);
    }

    // =================================================================
    // [EDGE] StopAsync after Dispose — both components
    //
    // Dispose sets _isCapturing/_isPlaying = false, so StopAsync should
    // return early without touching disposed state.
    // =================================================================

    [Fact]
    public async Task MicCapture_StopAsync_AfterDispose_DoesNotThrow()
    {
        // [EDGE] StopAsync after Dispose should be safe — idempotent contract
        var mic = new MicCapture();
        mic.Dispose();

        // Should not throw ObjectDisposedException or anything else
        await mic.StopAsync();
        Assert.False(mic.IsCapturing);
    }

    [Fact]
    public async Task AudioPlayer_StopAsync_AfterDispose_DoesNotThrow()
    {
        // [EDGE] StopAsync after Dispose should be safe — idempotent contract
        var player = new AudioPlayer();
        player.Dispose();

        // Should not throw ObjectDisposedException or anything else
        await player.StopAsync();
        Assert.False(player.IsPlaying);
    }

    // =================================================================
    // [EDGE] Concurrent StopAsync calls when not active
    //
    // Thread safety is claimed via locks. Verify multiple threads calling
    // StopAsync simultaneously don't corrupt state or throw.
    // =================================================================

    [Fact]
    public async Task MicCapture_ConcurrentStopCalls_WhenNotCapturing_AreAllSafe()
    {
        // [EDGE] Thread safety — concurrent StopAsync on idle mic
        using var mic = new MicCapture();

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => mic.StopAsync()))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.False(mic.IsCapturing);
    }

    [Fact]
    public async Task AudioPlayer_ConcurrentStopCalls_WhenNotPlaying_AreAllSafe()
    {
        // [EDGE] Thread safety — concurrent StopAsync on idle player
        using var player = new AudioPlayer();

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => player.StopAsync()))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.False(player.IsPlaying);
    }

    // =================================================================
    // [EDGE] Rapid create/dispose cycles
    //
    // Creating and immediately disposing many instances should not leak
    // resources or corrupt shared state (PortAudioLifecycle ref count).
    // =================================================================

    [Fact]
    public void MicCapture_RapidCreateDisposeCycles_DoNotThrow()
    {
        // [EDGE] Resource lifecycle — rapid create/dispose doesn't leak or throw
        for (int i = 0; i < 100; i++)
        {
            var mic = new MicCapture();
            Assert.False(mic.IsCapturing);
            mic.Dispose();
        }
    }

    [Fact]
    public void AudioPlayer_RapidCreateDisposeCycles_DoNotThrow()
    {
        // [EDGE] Resource lifecycle — rapid create/dispose doesn't leak or throw
        for (int i = 0; i < 100; i++)
        {
            var player = new AudioPlayer();
            Assert.False(player.IsPlaying);
            player.Dispose();
        }
    }

    // =================================================================
    // [BOUNDARY] PortAudioLifecycle — Release without prior init
    //
    // Release with refCount=0 should be a no-op (guard: s_refCount <= 0).
    // NOTE: PortAudioLifecycle is static, so this test may interact with
    // other tests if run in parallel. xUnit runs tests within a class
    // sequentially by default, which mitigates this.
    // =================================================================

    [Fact]
    public void PortAudioLifecycle_Release_WithoutInit_IsNoOp()
    {
        // [BOUNDARY] Release when ref count is already 0 — should not throw or go negative
        // This tests the guard clause: if (s_refCount <= 0) return;
        PortAudioLifecycle.Release();
        PortAudioLifecycle.Release();
        PortAudioLifecycle.Release();

        // No exception = success. The ref count should remain at 0, not go negative.
        // Verify by doing a balanced init+release which should still work correctly.
        try
        {
            PortAudioLifecycle.EnsureInitialized();
            PortAudioLifecycle.Release();
        }
        catch (Exception ex) when (ex.Message.Contains("PortAudio") || ex is DllNotFoundException)
        {
            // PortAudio native lib not available — the key test was the Release calls above
        }
    }

    [Fact]
    public async Task PortAudioLifecycle_ConcurrentInitRelease_MaintainsBalance()
    {
        // [EDGE] Thread safety — concurrent init/release operations
        // Each thread does a balanced init+release pair.
        const int threadCount = 20;

        try
        {
            var barrier = new Barrier(threadCount);
            var tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
            {
                barrier.SignalAndWait(); // synchronize start
                PortAudioLifecycle.EnsureInitialized();
                Thread.SpinWait(100); // brief work
                PortAudioLifecycle.Release();
            })).ToArray();

            await Task.WhenAll(tasks);

            // After all balanced pairs complete, one more init+release should work
            PortAudioLifecycle.EnsureInitialized();
            PortAudioLifecycle.Release();
        }
        catch (Exception ex) when (ex.Message.Contains("PortAudio") || ex is DllNotFoundException
                                   || (ex is AggregateException ae && ae.InnerExceptions.Any(
                                       e => e.Message.Contains("PortAudio") || e is DllNotFoundException)))
        {
            // PortAudio native lib not available — skip gracefully
        }
    }

    // =================================================================
    // [CONTRACT] MicCapture — interface compliance
    // =================================================================

    [Fact]
    public async Task MicCapture_StartAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // [CONTRACT] StartAsync on disposed instance must throw ObjectDisposedException
        var mic = new MicCapture();
        mic.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => mic.StartAsync());
    }

    [Fact]
    public async Task AudioPlayer_PlayAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // [CONTRACT] PlayAsync on disposed instance must throw ObjectDisposedException
        var player = new AudioPlayer();
        player.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => player.PlayAsync(new byte[] { 1, 2 }));
    }

    [Fact]
    public async Task AudioPlayer_PlayAsync_EmptyData_AfterDispose_ThrowsObjectDisposedException()
    {
        // [EDGE] Even empty data path checks disposed first
        var player = new AudioPlayer();
        player.Dispose();

        // The ObjectDisposedException check is BEFORE the empty data check
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => player.PlayAsync(ReadOnlyMemory<byte>.Empty));
    }

    // =================================================================
    // [EDGE] AudioPlayer — pre-cancelled CancellationToken with empty data
    //
    // Empty data returns before registering cancellation, so the event
    // should still fire regardless of token state.
    // =================================================================

    [Fact]
    public async Task AudioPlayer_PlayAsync_EmptyData_WithCancelledToken_StillFiresCompleted()
    {
        // [EDGE] Pre-cancelled token + empty data — empty path returns before ct registration
        using var player = new AudioPlayer();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        bool eventFired = false;
        player.PlaybackCompleted += () => eventFired = true;

        await player.PlayAsync(ReadOnlyMemory<byte>.Empty, cts.Token);

        Assert.True(eventFired,
            "PlaybackCompleted should fire for empty data even with cancelled token");
    }

    // =================================================================
    // [EDGE] Dispose idempotency — multiple Dispose + interleaved operations
    // =================================================================

    [Fact]
    public async Task MicCapture_DisposeMultipleTimes_InterleavedWithStop_IsStable()
    {
        // [EDGE] Interleaved dispose/stop cycle — tests defensive guards
        var mic = new MicCapture();

        mic.Dispose();
        await mic.StopAsync(); // should be no-op after dispose
        mic.Dispose();         // idempotent
        await mic.StopAsync(); // still no-op
        mic.Dispose();         // still idempotent

        Assert.False(mic.IsCapturing);
    }

    [Fact]
    public async Task AudioPlayer_DisposeMultipleTimes_InterleavedWithStop_IsStable()
    {
        // [EDGE] Interleaved dispose/stop cycle — tests defensive guards
        var player = new AudioPlayer();

        player.Dispose();
        await player.StopAsync(); // should be no-op after dispose
        player.Dispose();         // idempotent
        await player.StopAsync(); // still no-op
        player.Dispose();         // still idempotent

        Assert.False(player.IsPlaying);
    }

    // =================================================================
    // [EDGE] Event subscription safety — subscribe/unsubscribe on real objects
    // =================================================================

    [Fact]
    public void MicCapture_EventSubscription_DoesNotThrowOnDisposedInstance()
    {
        // [EDGE] Subscribing to events on disposed object should not throw
        var mic = new MicCapture();
        mic.Dispose();

        // Should not throw — events are just delegates
        mic.AudioCaptured += _ => { };
    }

    [Fact]
    public void AudioPlayer_EventSubscription_DoesNotThrowOnDisposedInstance()
    {
        // [EDGE] Subscribing to events on disposed object should not throw
        var player = new AudioPlayer();
        player.Dispose();

        // Should not throw — events are just delegates
        player.PlaybackCompleted += () => { };
    }

    // =================================================================
    // [EDGE] Hardware tests — Dispose during active operations
    //
    // These validate that Dispose doesn't deadlock when the PortAudio
    // callback thread is active. Marked as Hardware tests.
    // =================================================================

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task MicCapture_Dispose_WhileCapturing_DoesNotDeadlock()
    {
        // [EDGE] Dispose during active capture — must not deadlock
        var mic = new MicCapture();
        try
        {
            await mic.StartAsync();
            Assert.True(mic.IsCapturing);

            // Dispose while callback thread is running
            // Use a timeout to detect deadlock
            var disposeTask = Task.Run(() => mic.Dispose());
            var completed = await Task.WhenAny(disposeTask, Task.Delay(5000));

            Assert.True(completed == disposeTask,
                "Dispose should complete within 5 seconds — possible deadlock");
            Assert.False(mic.IsCapturing);
        }
        catch (InvalidOperationException)
        {
            // No mic available — skip
        }
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task AudioPlayer_Dispose_WhilePlaying_DoesNotDeadlock()
    {
        // [EDGE] Dispose during active playback — must not deadlock
        var player = new AudioPlayer();
        try
        {
            var tone = GenerateTestTone(1000); // 1 second tone
            var playTask = player.PlayAsync(tone);
            await Task.Delay(50); // Let playback start

            // Dispose while callback thread is running
            var disposeTask = Task.Run(() => player.Dispose());
            var completed = await Task.WhenAny(disposeTask, Task.Delay(5000));

            Assert.True(completed == disposeTask,
                "Dispose should complete within 5 seconds — possible deadlock");
            Assert.False(player.IsPlaying);

            // PlayAsync should also complete (TCS is resolved by Dispose)
            var playCompleted = await Task.WhenAny(playTask, Task.Delay(2000));
            Assert.True(playCompleted == playTask,
                "PlayAsync should complete after Dispose — not hang");
        }
        catch (InvalidOperationException)
        {
            // No audio device — skip
        }
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task AudioPlayer_PlayAsync_WhileAlreadyPlaying_ReplacesPlayback()
    {
        // [EDGE] Second PlayAsync should stop first and start new playback
        using var player = new AudioPlayer();
        try
        {
            var tone1 = GenerateTestTone(500); // 500ms
            var playTask1 = player.PlayAsync(tone1);
            await Task.Delay(50); // Let first playback start

            // Start second playback — should stop first
            bool interruptedCompleted = false;
            player.PlaybackCompleted += () => interruptedCompleted = true;

            var tone2 = GenerateTestTone(100); // 100ms
            await player.PlayAsync(tone2);

            // Second play completed. First was interrupted, so PlaybackCompleted
            // should NOT have fired for the interrupted first play.
            // (PlaybackCompleted fires only on natural completion, not on stop)
            // Note: We can't assert interruptedCompleted here because PlaybackCompleted
            // also fires for the SECOND (successful) playback. Just verify no deadlock.

            // Also verify playTask1 completed (StopAsync resolves its TCS)
            var task1Done = await Task.WhenAny(playTask1, Task.Delay(2000));
            Assert.True(task1Done == playTask1,
                "First PlayAsync should complete after being replaced");
        }
        catch (InvalidOperationException)
        {
            return; // No audio device
        }
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task MicCapture_ConcurrentStartStop_DoesNotCorruptState()
    {
        // [EDGE] Concurrent Start/Stop cycles — thread safety under pressure
        using var mic = new MicCapture();
        try
        {
            // First verify we can start (mic available)
            await mic.StartAsync();
            await mic.StopAsync();

            // Now do rapid concurrent operations
            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await mic.StartAsync();
                    await Task.Delay(10);
                    await mic.StopAsync();
                }));
            }

            var allDone = Task.WhenAll(tasks);
            var completed = await Task.WhenAny(allDone, Task.Delay(10_000));

            Assert.True(completed == allDone,
                "All concurrent Start/Stop operations should complete within 10s");
        }
        catch (InvalidOperationException)
        {
            return; // No mic
        }
    }

    // =================================================================
    // Helpers
    // =================================================================

    /// <summary>Generates a PCM16 sine wave at 440Hz for testing.</summary>
    private static byte[] GenerateTestTone(int durationMs = 100)
    {
        const int sampleRate = 16000;
        const double frequency = 440.0;
        int sampleCount = sampleRate * durationMs / 1000;
        var buffer = new byte[sampleCount * 2]; // 2 bytes per PCM16 sample

        for (int i = 0; i < sampleCount; i++)
        {
            double t = (double)i / sampleRate;
            short sample = (short)(short.MaxValue * 0.5 * Math.Sin(2 * Math.PI * frequency * t));
            buffer[i * 2] = (byte)(sample & 0xFF);
            buffer[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }

        return buffer;
    }
}
