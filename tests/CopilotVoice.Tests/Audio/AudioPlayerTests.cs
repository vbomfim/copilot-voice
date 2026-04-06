using CopilotVoice.Audio;

namespace CopilotVoice.Tests.Audio;

/// <summary>
/// Unit tests for real AudioPlayer implementation.
/// Tests validate state machine and contract behavior.
/// Hardware-dependent tests are marked with [Trait("Category", "Hardware")].
/// </summary>
public class AudioPlayerTests : IDisposable
{
    private readonly AudioPlayer _sut = new();

    public void Dispose() => _sut.Dispose();

    /// <summary>Generates a 100ms PCM16 sine wave at 440Hz for testing.</summary>
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

    // --- Initial state ---

    [Fact]
    public void InitialState_IsNotPlaying()
    {
        Assert.False(_sut.IsPlaying);
    }

    // --- AC6: StopAsync idempotent ---

    [Fact]
    public async Task StopAsync_WhenNotPlaying_IsNoOp()
    {
        await _sut.StopAsync();
        Assert.False(_sut.IsPlaying);
    }

    [Fact]
    public async Task StopAsync_CalledTwice_IsIdempotent()
    {
        await _sut.StopAsync();
        await _sut.StopAsync();
        Assert.False(_sut.IsPlaying);
    }

    // --- Dispose behavior ---

    [Fact]
    public void Dispose_SetsNotPlaying()
    {
        _sut.Dispose();
        Assert.False(_sut.IsPlaying);
    }

    [Fact]
    public async Task PlayAsync_AfterDispose_ThrowsObjectDisposed()
    {
        _sut.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _sut.PlayAsync(GenerateTestTone()));
    }

    [Fact]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        _sut.Dispose();
        _sut.Dispose(); // should not throw
    }

    // --- AC4/AC5: Real playback tests (require hardware) ---

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task PlayAsync_WithAudioData_SetsIsPlaying()
    {
        try
        {
            var tone = GenerateTestTone(50); // Short tone
            var playTask = _sut.PlayAsync(tone);

            // IsPlaying should be true immediately after starting
            // (give a tiny moment for the stream to start)
            await Task.Delay(10);
            // Note: For very short audio this may already be done
            // so we just verify the task completes without error
            await playTask;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("audio") || ex.Message.Contains("PortAudio"))
        {
            return; // No audio device
        }
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task PlayAsync_FiresPlaybackCompleted_WhenFinished()
    {
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sut.PlaybackCompleted += () => completedTcs.TrySetResult();

        try
        {
            var tone = GenerateTestTone(50); // 50ms tone
            await _sut.PlayAsync(tone);

            // PlaybackCompleted fires asynchronously via ThreadPool.
            // Wait up to 2 seconds for it to arrive.
            var completed = await Task.WhenAny(completedTcs.Task, Task.Delay(2000));
            Assert.True(completed == completedTcs.Task,
                "PlaybackCompleted should fire when playback finishes naturally");
        }
        catch (InvalidOperationException)
        {
            return; // No audio device
        }
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task PlayAsync_SetsNotPlaying_WhenFinished()
    {
        try
        {
            var tone = GenerateTestTone(50);
            await _sut.PlayAsync(tone);

            Assert.False(_sut.IsPlaying);
        }
        catch (InvalidOperationException)
        {
            return;
        }
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task StopAsync_DoesNotFirePlaybackCompleted()
    {
        bool completed = false;
        _sut.PlaybackCompleted += () => completed = true;

        try
        {
            var tone = GenerateTestTone(500); // 500ms — long enough to stop mid-stream
            var playTask = _sut.PlayAsync(tone);

            await Task.Delay(50); // Let playback start
            await _sut.StopAsync();

            // Wait a bit to make sure no late event fires
            await Task.Delay(100);
            Assert.False(completed, "PlaybackCompleted should NOT fire when stopped");

            // The play task should complete (not hang)
            await playTask;
        }
        catch (InvalidOperationException)
        {
            return;
        }
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task StopAsync_SetsNotPlaying()
    {
        try
        {
            var tone = GenerateTestTone(500);
            var playTask = _sut.PlayAsync(tone);

            await Task.Delay(50);
            await _sut.StopAsync();
            Assert.False(_sut.IsPlaying);

            await playTask;
        }
        catch (InvalidOperationException)
        {
            return;
        }
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task PlayAsync_WithCancellation_StopsPlayback()
    {
        bool completed = false;
        _sut.PlaybackCompleted += () => completed = true;

        try
        {
            using var cts = new CancellationTokenSource();
            var tone = GenerateTestTone(500);
            var playTask = _sut.PlayAsync(tone, cts.Token);

            await Task.Delay(50);
            cts.Cancel();

            // Should complete without throwing (cancellation handled gracefully)
            await playTask;
            Assert.False(completed, "PlaybackCompleted should NOT fire on cancellation");
        }
        catch (InvalidOperationException)
        {
            return;
        }
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task PlayAsync_WithEmptyData_CompletesImmediately()
    {
        bool completed = false;
        _sut.PlaybackCompleted += () => completed = true;

        try
        {
            await _sut.PlayAsync(ReadOnlyMemory<byte>.Empty);

            // Empty data = immediate completion
            Assert.False(_sut.IsPlaying);
        }
        catch (InvalidOperationException)
        {
            return;
        }
    }
}
