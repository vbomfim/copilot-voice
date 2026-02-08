namespace CopilotVoice.UI.Avatar;

/// <summary>
/// Drives idle animations (blink, yawn) and speaking mouth movement.
/// </summary>
public sealed class AvatarAnimator : IDisposable
{
    private static readonly TimeSpan BlinkInterval = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan YawnIdleThreshold = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan BlinkDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan YawnDuration = TimeSpan.FromMilliseconds(1200);

    private CancellationTokenSource? _idleCts;
    private DateTime _lastInteraction = DateTime.UtcNow;

    /// <summary>Raised whenever the animator wants to change the displayed expression.</summary>
    public event Action<AvatarExpression>? OnExpressionChanged;

    /// <summary>Start the idle animation loop (blink every ~8 s, yawn after ~20 s idle).</summary>
    public void StartIdleLoop()
    {
        StopIdleLoop();
        _idleCts = new CancellationTokenSource();
        _ = RunIdleLoopAsync(_idleCts.Token);
    }

    /// <summary>Stop the idle animation loop.</summary>
    public void StopIdleLoop()
    {
        _idleCts?.Cancel();
        _idleCts?.Dispose();
        _idleCts = null;
    }

    /// <summary>Record user/system activity to reset the yawn timer.</summary>
    public void RecordInteraction() => _lastInteraction = DateTime.UtcNow;

    /// <summary>Play a blink animation: HalfBlink → Blink → HalfBlink → Normal.</summary>
    public async Task BlinkAsync(CancellationToken ct = default)
    {
        OnExpressionChanged?.Invoke(AvatarExpression.HalfBlink);
        await Task.Delay(BlinkDuration / 2, ct);
        OnExpressionChanged?.Invoke(AvatarExpression.Blink);
        await Task.Delay(BlinkDuration, ct);
        OnExpressionChanged?.Invoke(AvatarExpression.HalfBlink);
        await Task.Delay(BlinkDuration / 2, ct);
        OnExpressionChanged?.Invoke(AvatarExpression.Normal);
    }

    /// <summary>Play a yawn animation: Yawn → YawnWide → Yawn → Normal.</summary>
    public async Task YawnAsync(CancellationToken ct = default)
    {
        OnExpressionChanged?.Invoke(AvatarExpression.Yawn);
        await Task.Delay(YawnDuration / 3, ct);
        OnExpressionChanged?.Invoke(AvatarExpression.YawnWide);
        await Task.Delay(YawnDuration / 3, ct);
        OnExpressionChanged?.Invoke(AvatarExpression.Yawn);
        await Task.Delay(YawnDuration / 3, ct);
        OnExpressionChanged?.Invoke(AvatarExpression.Normal);
    }

    /// <summary>Animate the speaking expression for a given duration.</summary>
    public async Task SpeakingAnimationAsync(double durationSeconds, CancellationToken ct = default)
    {
        var end = DateTime.UtcNow.AddSeconds(durationSeconds);
        var toggle = false;
        while (DateTime.UtcNow < end && !ct.IsCancellationRequested)
        {
            OnExpressionChanged?.Invoke(toggle ? AvatarExpression.Speaking : AvatarExpression.Normal);
            toggle = !toggle;
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }
        OnExpressionChanged?.Invoke(AvatarExpression.Normal);
    }

    public void Dispose() => StopIdleLoop();

    private async Task RunIdleLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(BlinkInterval, ct);
                if (ct.IsCancellationRequested) break;

                if (DateTime.UtcNow - _lastInteraction > YawnIdleThreshold)
                {
                    await YawnAsync(ct);
                    _lastInteraction = DateTime.UtcNow; // reset after yawn
                }
                else
                {
                    await BlinkAsync(ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[Animator] Idle loop error: {ex.Message}");
        }
    }
}
