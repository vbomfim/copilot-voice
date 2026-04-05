using CopilotVoice.Audio;

namespace CopilotVoice.Voice;

/// <summary>
/// Coordinates mic mute/unmute during voice response playback to prevent echo.
/// Mutes mic when response audio starts, unmutes after playback completes
/// with a configurable post-playback delay (default 500ms) to avoid catching
/// tail echo from speakers.
/// </summary>
public sealed class TurnManager
{
    private readonly IMicCapture _mic;

    /// <summary>Delay in ms after playback before unmuting mic (echo tail guard).</summary>
    internal const int PostPlaybackDelayMs = 500;

    public TurnManager(IMicCapture mic)
    {
        _mic = mic ?? throw new ArgumentNullException(nameof(mic));
    }

    /// <summary>Mute the microphone (stop capture).</summary>
    public async Task MuteMicAsync()
    {
        await _mic.StopAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Unmute the microphone after the post-playback delay.
    /// The delay prevents capturing echo from speakers.
    /// </summary>
    public async Task UnmuteMicAfterDelayAsync(CancellationToken ct = default)
    {
        await Task.Delay(PostPlaybackDelayMs, ct).ConfigureAwait(false);
        await _mic.StartAsync(ct).ConfigureAwait(false);
    }
}
