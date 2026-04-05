using CopilotVoice.UI.Avatar;
using CopilotVoice.Voice;

namespace CopilotVoice.Tests.UI;

/// <summary>
/// QA Guardian — edge case and boundary tests for AvatarStateManager.
/// These tests cover paths not exercised by the Developer Guardian's unit tests:
/// - Post-dispose safety (PTT events, manual expression, disconnect after Dispose)
/// - Duplicate PTT binding (double-fires)
/// - Manual override survives disconnect recovery
/// - Timeout fires ExpressionChanged event
/// - All expression values as manual overrides
/// - ExpressionChanged handler that throws
/// - SetDisconnected false when never disconnected
/// - AC2 Talk Mode stub behavior verification
/// </summary>
public class AvatarStateManagerEdgeCaseTests : IDisposable
{
    private readonly FakePushToTalkController _ptt = new();
    private readonly AvatarStateManager _manager;

    public AvatarStateManagerEdgeCaseTests()
    {
        _manager = new AvatarStateManager();
        _manager.BindToPushToTalk(_ptt);
    }

    public void Dispose()
    {
        _manager.Dispose();
    }

    // ---------------------------------------------------------------
    // [EDGE] Post-dispose safety — operations after Dispose should not crash
    // ---------------------------------------------------------------

    [Fact]
    public void AfterDispose_PttStateChange_DoesNotCrash()
    {
        _manager.Dispose();

        // PTT event fires after Dispose — should not throw
        // (event handler is still subscribed but timer is disposed)
        var ex = Record.Exception(() =>
            _ptt.SimulateStateChange(PushToTalkState.Recording));

        Assert.Null(ex);
    }

    [Fact]
    public void AfterDispose_SetManualExpression_DoesNotCrash()
    {
        _manager.Dispose();

        var ex = Record.Exception(() =>
            _manager.SetManualExpression(AvatarExpression.Focused));

        Assert.Null(ex);
    }

    [Fact]
    public void AfterDispose_SetDisconnected_DoesNotCrash()
    {
        _manager.Dispose();

        var ex = Record.Exception(() =>
            _manager.SetDisconnected(true));

        Assert.Null(ex);
    }

    [Fact]
    public void AfterDispose_TriggerTimeout_DoesNotCrash()
    {
        _manager.Dispose();

        var ex = Record.Exception(() =>
            _manager.TriggerTimeout());

        Assert.Null(ex);
    }

    // ---------------------------------------------------------------
    // [EDGE] Duplicate PTT binding — same controller bound twice
    // ---------------------------------------------------------------

    [Fact]
    public void DuplicateBindPtt_FiresDuplicateEvents()
    {
        // Bind the same controller again — now two subscriptions exist
        _manager.BindToPushToTalk(_ptt);

        var count = 0;
        _manager.ExpressionChanged += _ => count++;

        _ptt.SimulateStateChange(PushToTalkState.Recording);

        // BUG DETECTION: Two subscriptions means the handler fires twice.
        // First call: Normal→Listening (fires event, count=1)
        // Second call: Listening→Listening (same expression, deduplicated, count stays 1)
        // Actually: the second OnPttStateChanged sets _lastPttState=Recording again,
        // _manualOverride=null, calls UpdateExpression which sees same expression → no fire.
        // So event deduplication saves us from double-fire.
        Assert.Equal(1, count);
    }

    // ---------------------------------------------------------------
    // [EDGE] Manual override survives disconnect recovery
    // ---------------------------------------------------------------

    [Fact]
    public void ManualOverride_SurvivesDisconnectRecovery()
    {
        // Set manual override
        _manager.SetManualExpression(AvatarExpression.Focused);
        Assert.Equal(AvatarExpression.Focused, _manager.CurrentExpression);

        // Disconnect overrides to Concerned
        _manager.SetDisconnected(true);
        Assert.Equal(AvatarExpression.Concerned, _manager.CurrentExpression);

        // Reconnect — manual override should be restored
        // (disconnect only changes _isDisconnected, not _manualOverride)
        _manager.SetDisconnected(false);
        Assert.Equal(AvatarExpression.Focused, _manager.CurrentExpression);
    }

    // ---------------------------------------------------------------
    // [EDGE] Timeout fires ExpressionChanged event
    // ---------------------------------------------------------------

    [Fact]
    public void Timeout_FiresExpressionChanged()
    {
        _ptt.SimulateStateChange(PushToTalkState.Recording);
        Assert.Equal(AvatarExpression.Listening, _manager.CurrentExpression);

        AvatarExpression? received = null;
        _manager.ExpressionChanged += e => received = e;

        _manager.TriggerTimeout();

        Assert.Equal(AvatarExpression.Normal, received);
    }

    [Fact]
    public void Timeout_WhenAlreadyNormal_DoesNotFireEvent()
    {
        // Already Normal (initial state)
        Assert.Equal(AvatarExpression.Normal, _manager.CurrentExpression);

        var count = 0;
        _manager.ExpressionChanged += _ => count++;

        _manager.TriggerTimeout();

        Assert.Equal(0, count); // no change, no event
    }

    // ---------------------------------------------------------------
    // [BOUNDARY] SetManualExpression with various expression values
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(AvatarExpression.Listening)]
    [InlineData(AvatarExpression.Thinking)]
    [InlineData(AvatarExpression.Speaking)]
    [InlineData(AvatarExpression.Sleeping)]
    [InlineData(AvatarExpression.Concerned)]
    [InlineData(AvatarExpression.Smile)]
    [InlineData(AvatarExpression.Cry)]
    [InlineData(AvatarExpression.Muted)]
    public void SetManualExpression_AllValues_SetCorrectly(AvatarExpression expression)
    {
        _manager.SetManualExpression(expression);
        Assert.Equal(expression, _manager.CurrentExpression);
    }

    // ---------------------------------------------------------------
    // [EDGE] ExpressionChanged handler throws — should not break state
    // ---------------------------------------------------------------

    [Fact]
    public void ExpressionChanged_HandlerThrows_DoesNotPreventStateUpdate()
    {
        _manager.ExpressionChanged += _ => throw new InvalidOperationException("subscriber error");

        // Handler throws, but expression should still update
        Assert.Throws<InvalidOperationException>(() =>
            _ptt.SimulateStateChange(PushToTalkState.Recording));

        // Verify state was updated despite the exception
        Assert.Equal(AvatarExpression.Listening, _manager.CurrentExpression);
    }

    // ---------------------------------------------------------------
    // [BOUNDARY] SetDisconnected false when never disconnected
    // ---------------------------------------------------------------

    [Fact]
    public void SetDisconnected_False_WhenNeverDisconnected_IsNoOp()
    {
        var count = 0;
        _manager.ExpressionChanged += _ => count++;

        _manager.SetDisconnected(false); // already false

        Assert.Equal(0, count);
        Assert.Equal(AvatarExpression.Normal, _manager.CurrentExpression);
    }

    // ---------------------------------------------------------------
    // [COVERAGE] AC2: Talk Mode stub — BindToTalkMode does NOT subscribe to events
    // Documents the known gap: Talk Mode state changes do NOT affect avatar
    // ---------------------------------------------------------------

    [Fact]
    public void BindToTalkMode_NonNull_DoesNotSubscribeToEvents()
    {
        var fakeTalkMode = new FakeTalkModeController();
        _manager.BindToTalkMode(fakeTalkMode);

        var count = 0;
        _manager.ExpressionChanged += _ => count++;

        // Simulate Talk Mode activation — should NOT affect avatar
        // because BindToTalkMode is a no-op stub
        fakeTalkMode.SimulateStateChanged(TalkModeState.Listening);

        Assert.Equal(0, count);
        Assert.Equal(AvatarExpression.Normal, _manager.CurrentExpression);
    }

    [Fact]
    public void BindToTalkMode_NonNull_TalkModeChangesIgnored()
    {
        var fakeTalkMode = new FakeTalkModeController();
        _manager.BindToTalkMode(fakeTalkMode);

        // AC2 requires: Talk Mode Listening → Processing → Speaking → Listening
        // maps to avatar expressions. But this is NOT implemented yet.
        // Verify the stub behavior: Talk Mode changes have zero effect.
        fakeTalkMode.SimulateStateChanged(TalkModeState.Listening);
        Assert.Equal(AvatarExpression.Normal, _manager.CurrentExpression);

        fakeTalkMode.SimulateStateChanged(TalkModeState.Off);
        Assert.Equal(AvatarExpression.Normal, _manager.CurrentExpression);
    }

    // ---------------------------------------------------------------
    // [EDGE] Reconnect during active PTT state
    // ---------------------------------------------------------------

    [Fact]
    public void Reconnect_DuringActivePttState_RestoresPttExpression()
    {
        // PTT is Recording → Listening
        _ptt.SimulateStateChange(PushToTalkState.Recording);
        Assert.Equal(AvatarExpression.Listening, _manager.CurrentExpression);

        // Disconnect → Concerned
        _manager.SetDisconnected(true);
        Assert.Equal(AvatarExpression.Concerned, _manager.CurrentExpression);

        // Reconnect while PTT is still Recording → should restore Listening
        _manager.SetDisconnected(false);
        Assert.Equal(AvatarExpression.Listening, _manager.CurrentExpression);
    }

    // ---------------------------------------------------------------
    // [EDGE] Priority: Disconnect > Manual > PTT — full sequence
    // ---------------------------------------------------------------

    [Fact]
    public void FullPrioritySequence_DisconnectManualPtt()
    {
        var expressions = new List<AvatarExpression>();
        _manager.ExpressionChanged += e => expressions.Add(e);

        // 1. PTT Recording → Listening (lowest priority)
        _ptt.SimulateStateChange(PushToTalkState.Recording);
        Assert.Equal(AvatarExpression.Listening, _manager.CurrentExpression);

        // 2. Manual override → Focused (mid priority, overrides PTT)
        _manager.SetManualExpression(AvatarExpression.Focused);
        Assert.Equal(AvatarExpression.Focused, _manager.CurrentExpression);

        // 3. Disconnect → Concerned (highest priority, overrides manual)
        _manager.SetDisconnected(true);
        Assert.Equal(AvatarExpression.Concerned, _manager.CurrentExpression);

        // 4. PTT changes during disconnect — stays Concerned
        _ptt.SimulateStateChange(PushToTalkState.Processing);
        Assert.Equal(AvatarExpression.Concerned, _manager.CurrentExpression);

        // 5. Reconnect — PTT change cleared manual override, so falls to PTT mapping
        _manager.SetDisconnected(false);
        // PTT Processing → Thinking (manual override was cleared by PTT change in step 4)
        Assert.Equal(AvatarExpression.Thinking, _manager.CurrentExpression);
    }

    // ---------------------------------------------------------------
    // [EDGE] Multiple dispose calls — should be safe
    // ---------------------------------------------------------------

    [Fact]
    public void DoubleDispose_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
        {
            _manager.Dispose();
            _manager.Dispose();
        });

        Assert.Null(ex);
    }

    // ---------------------------------------------------------------
    // [EDGE] Unbound manager — SetManualExpression and SetDisconnected
    // ---------------------------------------------------------------

    [Fact]
    public void UnboundManager_ManualExpressionWorks()
    {
        using var unboundManager = new AvatarStateManager();

        unboundManager.SetManualExpression(AvatarExpression.Sleeping);
        Assert.Equal(AvatarExpression.Sleeping, unboundManager.CurrentExpression);
    }

    [Fact]
    public void UnboundManager_DisconnectedWorks()
    {
        using var unboundManager = new AvatarStateManager();

        unboundManager.SetDisconnected(true);
        Assert.Equal(AvatarExpression.Concerned, unboundManager.CurrentExpression);
    }
}
