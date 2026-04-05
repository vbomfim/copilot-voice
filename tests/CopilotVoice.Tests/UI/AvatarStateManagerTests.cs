using CopilotVoice.UI.Avatar;
using CopilotVoice.Voice;

namespace CopilotVoice.Tests.UI;

public class AvatarStateManagerTests : IDisposable
{
    private readonly FakePushToTalkController _ptt = new();
    private readonly AvatarStateManager _manager;

    public AvatarStateManagerTests()
    {
        _manager = new AvatarStateManager();
        _manager.BindToPushToTalk(_ptt);
    }

    public void Dispose()
    {
        _manager.Dispose();
    }

    // --- AC1: Push-to-talk state mapping ---

    [Fact]
    public void InitialExpression_IsNormal()
    {
        Assert.Equal(AvatarExpression.Normal, _manager.CurrentExpression);
    }

    [Fact]
    public void PttIdle_MapsToNormal()
    {
        _ptt.SimulateStateChange(PushToTalkState.Recording);
        _ptt.SimulateStateChange(PushToTalkState.Idle);
        Assert.Equal(AvatarExpression.Normal, _manager.CurrentExpression);
    }

    [Fact]
    public void PttRecording_MapsToListening()
    {
        _ptt.SimulateStateChange(PushToTalkState.Recording);
        Assert.Equal(AvatarExpression.Listening, _manager.CurrentExpression);
    }

    [Fact]
    public void PttProcessing_MapsToThinking()
    {
        _ptt.SimulateStateChange(PushToTalkState.Processing);
        Assert.Equal(AvatarExpression.Thinking, _manager.CurrentExpression);
    }

    [Fact]
    public void PttPlaying_MapsToSpeaking()
    {
        _ptt.SimulateStateChange(PushToTalkState.Playing);
        Assert.Equal(AvatarExpression.Speaking, _manager.CurrentExpression);
    }

    [Fact]
    public void FullCycle_Idle_Recording_Processing_Playing_Idle()
    {
        var expressions = new List<AvatarExpression>();
        _manager.ExpressionChanged += e => expressions.Add(e);

        _ptt.SimulateStateChange(PushToTalkState.Recording);
        _ptt.SimulateStateChange(PushToTalkState.Processing);
        _ptt.SimulateStateChange(PushToTalkState.Playing);
        _ptt.SimulateStateChange(PushToTalkState.Idle);

        Assert.Equal(new[]
        {
            AvatarExpression.Listening,
            AvatarExpression.Thinking,
            AvatarExpression.Speaking,
            AvatarExpression.Normal,
        }, expressions);
    }

    [Fact]
    public void PttStateChange_FiresExpressionChanged()
    {
        AvatarExpression? received = null;
        _manager.ExpressionChanged += e => received = e;

        _ptt.SimulateStateChange(PushToTalkState.Recording);

        Assert.Equal(AvatarExpression.Listening, received);
    }

    // --- AC3: Manual expression override ---

    [Fact]
    public void SetManualExpression_OverridesCurrentState()
    {
        _ptt.SimulateStateChange(PushToTalkState.Idle);

        _manager.SetManualExpression(AvatarExpression.Focused);

        Assert.Equal(AvatarExpression.Focused, _manager.CurrentExpression);
    }

    [Fact]
    public void ManualExpression_ClearedOnNextPttStateChange()
    {
        _manager.SetManualExpression(AvatarExpression.Focused);
        Assert.Equal(AvatarExpression.Focused, _manager.CurrentExpression);

        _ptt.SimulateStateChange(PushToTalkState.Recording);
        Assert.Equal(AvatarExpression.Listening, _manager.CurrentExpression);
    }

    [Fact]
    public void ManualExpression_FiresExpressionChanged()
    {
        AvatarExpression? received = null;
        _manager.ExpressionChanged += e => received = e;

        _manager.SetManualExpression(AvatarExpression.Relaxed);

        Assert.Equal(AvatarExpression.Relaxed, received);
    }

    // --- AC6: Disconnect overlay ---

    [Fact]
    public void SetDisconnected_True_SetsConcerned()
    {
        _manager.SetDisconnected(true);

        Assert.Equal(AvatarExpression.Concerned, _manager.CurrentExpression);
    }

    [Fact]
    public void Disconnected_OverridesPttState()
    {
        _ptt.SimulateStateChange(PushToTalkState.Recording);
        _manager.SetDisconnected(true);

        Assert.Equal(AvatarExpression.Concerned, _manager.CurrentExpression);
    }

    [Fact]
    public void Disconnected_OverridesManualExpression()
    {
        _manager.SetManualExpression(AvatarExpression.Focused);
        _manager.SetDisconnected(true);

        Assert.Equal(AvatarExpression.Concerned, _manager.CurrentExpression);
    }

    [Fact]
    public void Disconnected_PttStateChangesStillConcerned()
    {
        _manager.SetDisconnected(true);
        _ptt.SimulateStateChange(PushToTalkState.Recording);

        Assert.Equal(AvatarExpression.Concerned, _manager.CurrentExpression);
    }

    [Fact]
    public void SetDisconnected_False_RevertsToMappedState()
    {
        _ptt.SimulateStateChange(PushToTalkState.Recording);
        _manager.SetDisconnected(true);
        Assert.Equal(AvatarExpression.Concerned, _manager.CurrentExpression);

        _manager.SetDisconnected(false);
        Assert.Equal(AvatarExpression.Listening, _manager.CurrentExpression);
    }

    [Fact]
    public void SetDisconnected_False_WhenIdle_RevertsToNormal()
    {
        _manager.SetDisconnected(true);
        _manager.SetDisconnected(false);

        Assert.Equal(AvatarExpression.Normal, _manager.CurrentExpression);
    }

    [Fact]
    public void SetDisconnected_True_FiresExpressionChanged()
    {
        AvatarExpression? received = null;
        _manager.ExpressionChanged += e => received = e;

        _manager.SetDisconnected(true);

        Assert.Equal(AvatarExpression.Concerned, received);
    }

    // --- Timeout: 60s inactivity → Normal ---

    [Fact]
    public void Timeout_TransitionsToNormal()
    {
        // Use the internal timeout method for deterministic testing
        _ptt.SimulateStateChange(PushToTalkState.Recording);
        Assert.Equal(AvatarExpression.Listening, _manager.CurrentExpression);

        _manager.TriggerTimeout();

        Assert.Equal(AvatarExpression.Normal, _manager.CurrentExpression);
    }

    [Fact]
    public void Timeout_DoesNotOverrideDisconnected()
    {
        _manager.SetDisconnected(true);
        _manager.TriggerTimeout();

        Assert.Equal(AvatarExpression.Concerned, _manager.CurrentExpression);
    }

    // --- Rapid state changes: latest wins ---

    [Fact]
    public void RapidStateChanges_LatestWins()
    {
        _ptt.SimulateStateChange(PushToTalkState.Recording);
        _ptt.SimulateStateChange(PushToTalkState.Processing);
        _ptt.SimulateStateChange(PushToTalkState.Playing);

        Assert.Equal(AvatarExpression.Speaking, _manager.CurrentExpression);
    }

    [Fact]
    public void RapidStateChanges_AllEventsFireInOrder()
    {
        var expressions = new List<AvatarExpression>();
        _manager.ExpressionChanged += e => expressions.Add(e);

        _ptt.SimulateStateChange(PushToTalkState.Recording);
        _ptt.SimulateStateChange(PushToTalkState.Processing);
        _ptt.SimulateStateChange(PushToTalkState.Playing);

        Assert.Equal(new[]
        {
            AvatarExpression.Listening,
            AvatarExpression.Thinking,
            AvatarExpression.Speaking,
        }, expressions);
    }

    // --- BindToTalkMode with null ---

    [Fact]
    public void BindToTalkMode_Null_DoesNotThrow()
    {
        var ex = Record.Exception(() => _manager.BindToTalkMode(null));
        Assert.Null(ex);
    }

    [Fact]
    public void BindToTalkMode_WithController_DoesNotThrow()
    {
        var fakeTalkMode = new FakeTalkModeController();
        var ex = Record.Exception(() => _manager.BindToTalkMode(fakeTalkMode));
        Assert.Null(ex);
    }

    // --- Edge cases ---

    [Fact]
    public void SamePttState_DoesNotFireDuplicate()
    {
        var count = 0;
        _manager.ExpressionChanged += _ => count++;

        _ptt.SimulateStateChange(PushToTalkState.Recording);
        _ptt.SimulateStateChange(PushToTalkState.Recording);

        Assert.Equal(1, count);
    }

    [Fact]
    public void ManagerWithoutPttBinding_StaysNormal()
    {
        var unboundManager = new AvatarStateManager();
        Assert.Equal(AvatarExpression.Normal, unboundManager.CurrentExpression);
        unboundManager.Dispose();
    }

    [Fact]
    public void SetManualExpression_DoesNotFireIfSameAsCurrentExpression()
    {
        var count = 0;
        _manager.ExpressionChanged += _ => count++;

        // Current is Normal (initial)
        _manager.SetManualExpression(AvatarExpression.Normal);

        Assert.Equal(0, count);
    }

    [Fact]
    public void SetDisconnected_TrueRepeatedly_FiresOnlyOnce()
    {
        var count = 0;
        _manager.ExpressionChanged += _ => count++;

        _manager.SetDisconnected(true);
        _manager.SetDisconnected(true);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Timeout_AfterManualOverride_RevertsToNormal()
    {
        _manager.SetManualExpression(AvatarExpression.Focused);
        _manager.TriggerTimeout();

        Assert.Equal(AvatarExpression.Normal, _manager.CurrentExpression);
    }
}

// --- Fakes ---

internal sealed class FakePushToTalkController : IPushToTalkController
{
    private PushToTalkState _state = PushToTalkState.Idle;

    public PushToTalkState State => _state;
    public event Action<PushToTalkState>? StateChanged;

    public void SimulateStateChange(PushToTalkState newState)
    {
        _state = newState;
        StateChanged?.Invoke(newState);
    }

    public void OnHotkeyPressed() { }
    public void OnHotkeyReleased() { }
    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
}

internal sealed class FakeTalkModeController : ITalkModeController
{
    private TalkModeState _state = TalkModeState.Off;

    public TalkModeState State => _state;
    public bool IsActive => _state != TalkModeState.Off;
    public event Action<TalkModeState>? StateChanged;

    public Task ActivateAsync(CancellationToken ct = default)
    {
        _state = TalkModeState.Listening;
        StateChanged?.Invoke(_state);
        return Task.CompletedTask;
    }

    public Task DeactivateAsync()
    {
        _state = TalkModeState.Off;
        StateChanged?.Invoke(_state);
        return Task.CompletedTask;
    }

    public void SimulateStateChanged(TalkModeState state)
    {
        _state = state;
        StateChanged?.Invoke(state);
    }
}
