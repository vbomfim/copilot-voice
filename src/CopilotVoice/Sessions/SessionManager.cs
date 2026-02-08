namespace CopilotVoice.Sessions;

public class SessionManager : IDisposable
{
    private readonly SessionDetector _detector;
    private CopilotSession? _lockedSession;
    private CopilotSession? _currentTarget;
    private CancellationTokenSource? _watchCts;
    private bool _disposed;

    public SessionTargetMode Mode { get; private set; } = SessionTargetMode.AutoFollow;

    public event Action<CopilotSession?>? OnTargetChanged;

    public SessionManager(SessionDetector detector)
    {
        _detector = detector;
    }

    public void LockToSession(CopilotSession session)
    {
        Mode = SessionTargetMode.Locked;
        _lockedSession = session;
        UpdateTarget(session);
    }

    public void Unlock()
    {
        Mode = SessionTargetMode.AutoFollow;
        _lockedSession = null;
    }

    public bool IsLocked => Mode == SessionTargetMode.Locked;

    public void ToggleLock()
    {
        if (IsLocked)
            Unlock();
        else if (_currentTarget != null)
            LockToSession(_currentTarget);
    }

    public void SelectSession(CopilotSession session)
    {
        LockToSession(session);
    }

    public CopilotSession? GetTargetSession()
    {
        if (Mode == SessionTargetMode.Locked)
            return _lockedSession;

        var focused = _detector.GetFocusedSession();
        if (focused != null && !IsSelf(focused))
            return focused;
        return _currentTarget;
    }

    public void StartWatching(int pollIntervalMs = 500)
    {
        // Default to first available session if none set
        if (_currentTarget == null)
        {
            var sessions = _detector.GetCachedSessions();
            if (sessions.Count == 0)
                sessions = _detector.DetectSessions();
            // Prefer sessions that aren't copilot-voice itself
            var candidate = sessions.FirstOrDefault(s => !IsSelf(s))
                ?? sessions.FirstOrDefault();
            if (candidate != null)
                UpdateTarget(candidate);
        }

        _watchCts = new CancellationTokenSource();
        _ = WatchLoopAsync(pollIntervalMs, _watchCts.Token);
    }

    public void StopWatching()
    {
        _watchCts?.Cancel();
    }

    private async Task WatchLoopAsync(int intervalMs, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (Mode == SessionTargetMode.AutoFollow)
                {
                    var focused = _detector.GetFocusedSession();
                    if (focused != null && !IsSelf(focused) && focused.Id != _currentTarget?.Id)
                        UpdateTarget(focused);
                }
                else if (Mode == SessionTargetMode.Locked && _lockedSession != null)
                {
                    // Check if locked session is still alive
                    var sessions = _detector.GetCachedSessions();
                    if (!sessions.Any(s => s.Id == _lockedSession.Id))
                    {
                        Unlock();
                        OnTargetChanged?.Invoke(null);
                    }
                }
            }
            catch { /* ignore polling errors */ }
            await Task.Delay(intervalMs, ct).ConfigureAwait(false);
        }
    }

    private void UpdateTarget(CopilotSession session)
    {
        _currentTarget = session;
        OnTargetChanged?.Invoke(session);
    }

    private static bool IsSelf(CopilotSession session) =>
        session.Label.Contains("copilot-voice", StringComparison.OrdinalIgnoreCase) ||
        session.ProcessId == Environment.ProcessId;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watchCts?.Cancel();
        _watchCts?.Dispose();
    }
}
