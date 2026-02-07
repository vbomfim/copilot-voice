using CopilotVoice.Pomodoro;
using Xunit;

namespace CopilotVoice.Tests.Pomodoro;

public class PomodoroTimerTests
{
    [Fact]
    public void Start_SetsPhaseToWork()
    {
        var timer = new PomodoroTimer(25, 5);
        timer.Start();
        Assert.Equal(PomodoroPhase.Work, timer.CurrentPhase);
        Assert.True(timer.IsRunning);
        timer.Stop();
    }

    [Fact]
    public void Stop_FiresOnPhaseChanged_WithStopped()
    {
        var timer = new PomodoroTimer(25, 5);
        PomodoroPhase? receivedPhase = null;
        timer.OnPhaseChanged += (phase) => receivedPhase = phase;

        timer.Start();
        timer.Stop();

        Assert.Equal(PomodoroPhase.Stopped, receivedPhase);
        Assert.Equal(TimeSpan.Zero, timer.Remaining);
        Assert.False(timer.IsRunning);
    }

    [Fact]
    public void Stop_FiresOnTick_WithZero()
    {
        var timer = new PomodoroTimer(25, 5);
        TimeSpan? receivedTick = null;
        timer.OnTick += (remaining) => receivedTick = remaining;

        timer.Start();
        timer.Stop();

        Assert.Equal(TimeSpan.Zero, receivedTick);
    }

    [Fact]
    public void Pause_PreservesCurrentPhase()
    {
        var timer = new PomodoroTimer(25, 5);
        timer.Start();
        Assert.Equal(PomodoroPhase.Work, timer.CurrentPhase);

        timer.Pause();
        Assert.Equal(PomodoroPhase.Paused, timer.CurrentPhase);

        timer.Stop();
    }

    [Fact]
    public void Resume_RestoresPhaseBeforePause()
    {
        // Validates the fix: Resume() restores the phase active before Pause(),
        // not hardcoded to Work
        var timer = new PomodoroTimer(1, 1);

        timer.Start();
        Assert.Equal(PomodoroPhase.Work, timer.CurrentPhase);

        timer.Pause();
        timer.Resume();
        Assert.Equal(PomodoroPhase.Work, timer.CurrentPhase);
        Assert.True(timer.IsRunning);

        timer.Stop();
    }

    [Fact]
    public void Constructor_SetsCorrectDurations()
    {
        var timer = new PomodoroTimer(30, 10);
        Assert.Equal(30, timer.WorkMinutes);
        Assert.Equal(10, timer.BreakMinutes);
    }

    [Fact]
    public void Stop_WhenNotStarted_DoesNotThrow()
    {
        var timer = new PomodoroTimer(25, 5);
        var ex = Record.Exception(() => timer.Stop());
        Assert.Null(ex);
    }

    [Fact]
    public void Pause_WhenNotStarted_DoesNotThrow()
    {
        var timer = new PomodoroTimer(25, 5);
        var ex = Record.Exception(() => timer.Pause());
        Assert.Null(ex);
    }
}
