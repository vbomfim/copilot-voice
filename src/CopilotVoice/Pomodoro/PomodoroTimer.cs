namespace CopilotVoice.Pomodoro;

public class PomodoroTimer : IDisposable
{
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public int WorkMinutes { get; }
    public int BreakMinutes { get; }
    public PomodoroPhase CurrentPhase { get; private set; } = PomodoroPhase.Stopped;
    public TimeSpan Remaining { get; private set; }
    public bool IsRunning => CurrentPhase is PomodoroPhase.Work or PomodoroPhase.Break;

    public event Action<PomodoroPhase>? OnPhaseChanged;
    public event Action<TimeSpan>? OnTick;

    public PomodoroTimer(int workMinutes = 25, int breakMinutes = 5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(workMinutes, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(breakMinutes, 0);

        WorkMinutes = workMinutes;
        BreakMinutes = breakMinutes;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        CurrentPhase = PomodoroPhase.Stopped;
        Remaining = TimeSpan.Zero;
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning)
        {
            _cts?.Cancel();
            CurrentPhase = PomodoroPhase.Paused;
            OnPhaseChanged?.Invoke(CurrentPhase);
        }
    }

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (CurrentPhase != PomodoroPhase.Paused)
            return;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CurrentPhase = PomodoroPhase.Work;
        OnPhaseChanged?.Invoke(CurrentPhase);
        _ = CountdownAsync(Remaining, _cts.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Work phase
            CurrentPhase = PomodoroPhase.Work;
            OnPhaseChanged?.Invoke(CurrentPhase);
            await CountdownAsync(TimeSpan.FromMinutes(WorkMinutes), ct);
            if (ct.IsCancellationRequested) break;

            // Break phase
            CurrentPhase = PomodoroPhase.Break;
            OnPhaseChanged?.Invoke(CurrentPhase);
            await CountdownAsync(TimeSpan.FromMinutes(BreakMinutes), ct);
        }
    }

    private async Task CountdownAsync(TimeSpan duration, CancellationToken ct)
    {
        Remaining = duration;
        OnTick?.Invoke(Remaining);

        while (Remaining > TimeSpan.Zero && !ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            Remaining -= TimeSpan.FromSeconds(1);
            if (Remaining < TimeSpan.Zero)
                Remaining = TimeSpan.Zero;

            OnTick?.Invoke(Remaining);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        GC.SuppressFinalize(this);
    }
}

public enum PomodoroPhase
{
    Work,
    Break,
    Stopped,
    Paused
}
