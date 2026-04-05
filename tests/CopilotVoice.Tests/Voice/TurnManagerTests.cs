using CopilotVoice.Audio;

namespace CopilotVoice.Tests.Voice;

/// <summary>
/// Unit tests for TurnManager — mic mute/unmute coordination
/// during voice response playback for echo prevention.
/// </summary>
public class TurnManagerTests
{
    private readonly FakeMicCapture _mic = new();

    // --- Constructor validation ---

    [Fact]
    public void Ctor_NullMic_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CopilotVoice.Voice.TurnManager(null!));
    }

    // --- MuteMicAsync ---

    [Fact]
    public async Task MuteMicAsync_StopsMicCapture()
    {
        var mic = new FakeMicCapture();
        // Simulate mic already capturing
        await mic.StartAsync();
        Assert.True(mic.IsCapturing);

        var turnManager = new CopilotVoice.Voice.TurnManager(mic);
        await turnManager.MuteMicAsync();

        Assert.False(mic.IsCapturing);
    }

    [Fact]
    public async Task MuteMicAsync_WhenAlreadyStopped_IsIdempotent()
    {
        var mic = new FakeMicCapture();
        Assert.False(mic.IsCapturing);

        var turnManager = new CopilotVoice.Voice.TurnManager(mic);
        await turnManager.MuteMicAsync();

        Assert.False(mic.IsCapturing);
    }

    // --- UnmuteMicAfterDelayAsync ---

    [Fact]
    public async Task UnmuteMicAfterDelayAsync_StartsMicCapture()
    {
        var mic = new FakeMicCapture();
        var turnManager = new CopilotVoice.Voice.TurnManager(mic);

        await turnManager.UnmuteMicAfterDelayAsync();

        Assert.True(mic.IsCapturing);
    }

    [Fact]
    public async Task UnmuteMicAfterDelayAsync_WaitsBeforeStart()
    {
        var mic = new FakeMicCapture();
        var turnManager = new CopilotVoice.Voice.TurnManager(mic);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await turnManager.UnmuteMicAfterDelayAsync();
        sw.Stop();

        // Should have waited at least the post-playback delay
        Assert.True(sw.ElapsedMilliseconds >= 400,
            $"Expected at least ~500ms delay, got {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task UnmuteMicAfterDelayAsync_CancellationToken_Cancels()
    {
        var mic = new FakeMicCapture();
        var turnManager = new CopilotVoice.Voice.TurnManager(mic);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => turnManager.UnmuteMicAfterDelayAsync(cts.Token));

        Assert.False(mic.IsCapturing, "Mic should NOT start if cancelled");
    }

    [Fact]
    public async Task UnmuteMicAfterDelayAsync_CancelledDuringDelay_DoesNotStartMic()
    {
        var mic = new FakeMicCapture();
        var turnManager = new CopilotVoice.Voice.TurnManager(mic);

        using var cts = new CancellationTokenSource();
        var task = turnManager.UnmuteMicAfterDelayAsync(cts.Token);

        // Cancel shortly after starting
        await Task.Delay(50);
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
        Assert.False(mic.IsCapturing, "Mic should NOT start if cancelled during delay");
    }

    // --- PostPlaybackDelayMs constant ---

    [Fact]
    public void PostPlaybackDelayMs_Is500()
    {
        Assert.Equal(500, CopilotVoice.Voice.TurnManager.PostPlaybackDelayMs);
    }
}
