using CopilotVoice.Voice;

namespace CopilotVoice.UI.Avatar;

/// <summary>
/// Contract for mapping voice pipeline states to avatar expressions.
/// </summary>
public interface IAvatarStateManager : IDisposable
{
    /// <summary>The currently active avatar expression.</summary>
    AvatarExpression CurrentExpression { get; }

    /// <summary>Raised when the expression changes.</summary>
    event Action<AvatarExpression>? ExpressionChanged;

    /// <summary>Subscribe to push-to-talk state changes.</summary>
    void BindToPushToTalk(IPushToTalkController controller);

    /// <summary>Subscribe to Talk Mode state changes (nullable — Talk Mode may not exist yet).</summary>
    void BindToTalkMode(ITalkModeController? controller);

    /// <summary>Set a manual expression (e.g. from set_avatar function call). Stays until next automatic state transition.</summary>
    void SetManualExpression(AvatarExpression expression);

    /// <summary>Set disconnected state. When true, expression is Concerned regardless of other state.</summary>
    void SetDisconnected(bool disconnected);
}

/// <summary>
/// Maps voice pipeline states to avatar expressions.
/// Handles push-to-talk state mapping, manual overrides, disconnect overlay, and inactivity timeout.
/// </summary>
public sealed class AvatarStateManager : IAvatarStateManager
{
    private static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(60);

    private readonly object _lock = new();
    private readonly Timer _timeoutTimer;

    private AvatarExpression _currentExpression = AvatarExpression.Normal;
    private AvatarExpression? _manualOverride;
    private PushToTalkState _lastPttState = PushToTalkState.Idle;
    private bool _isDisconnected;
    private bool _disposed;

    public AvatarExpression CurrentExpression
    {
        get { lock (_lock) return _currentExpression; }
    }

    public event Action<AvatarExpression>? ExpressionChanged;

    public AvatarStateManager()
    {
        _timeoutTimer = new Timer(OnTimeout, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void BindToPushToTalk(IPushToTalkController controller)
    {
        controller.StateChanged += OnPttStateChanged;
    }

    public void BindToTalkMode(ITalkModeController? controller)
    {
        // Talk Mode (#64) is optional — no-op if null
        if (controller is null) return;

        // Future: subscribe to controller.ActiveChanged
    }

    public void SetManualExpression(AvatarExpression expression)
    {
        lock (_lock)
        {
            if (expression == _currentExpression) return;

            _manualOverride = expression;
            UpdateExpression();
        }
    }

    public void SetDisconnected(bool disconnected)
    {
        lock (_lock)
        {
            if (_isDisconnected == disconnected) return;

            _isDisconnected = disconnected;
            UpdateExpression();
        }
    }

    /// <summary>
    /// Triggers the inactivity timeout. Exposed as internal for deterministic testing.
    /// </summary>
    internal void TriggerTimeout()
    {
        lock (_lock)
        {
            // Don't override disconnected state
            if (_isDisconnected) return;

            _manualOverride = null;
            _lastPttState = PushToTalkState.Idle;
            UpdateExpression();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timeoutTimer.Dispose();
    }

    private void OnPttStateChanged(PushToTalkState state)
    {
        lock (_lock)
        {
            _lastPttState = state;
            _manualOverride = null; // clear manual override on any automatic transition
            ResetTimeout();
            UpdateExpression();
        }
    }

    private void OnTimeout(object? state) => TriggerTimeout();

    /// <summary>
    /// Resolves the effective expression based on priority:
    /// 1. Disconnected → Concerned (highest priority)
    /// 2. Manual override
    /// 3. PTT state mapping
    /// </summary>
    private void UpdateExpression()
    {
        // Must be called under _lock

        AvatarExpression resolved;
        if (_isDisconnected)
        {
            resolved = AvatarExpression.Concerned;
        }
        else if (_manualOverride.HasValue)
        {
            resolved = _manualOverride.Value;
        }
        else
        {
            resolved = MapPttState(_lastPttState);
        }

        if (resolved == _currentExpression) return;

        _currentExpression = resolved;
        ExpressionChanged?.Invoke(resolved);
    }

    private static AvatarExpression MapPttState(PushToTalkState state) => state switch
    {
        PushToTalkState.Idle => AvatarExpression.Normal,
        PushToTalkState.Recording => AvatarExpression.Listening,
        PushToTalkState.Processing => AvatarExpression.Thinking,
        PushToTalkState.Playing => AvatarExpression.Speaking,
        _ => AvatarExpression.Normal,
    };

    private void ResetTimeout()
    {
        try
        {
            _timeoutTimer.Change(InactivityTimeout, System.Threading.Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException) { }
    }
}
