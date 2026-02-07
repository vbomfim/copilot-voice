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

    public CopilotSession? GetTargetSession()
    {
        if (Mode == SessionTargetMode.Locked)
            return _lockedSession;

        return _detector.GetFocusedSession() ?? _currentTarget;
    }

    public void StartWatching(int pollIntervalMs = 500)
    {
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
                    if (focused != null && focused.Id != _currentTarget?.Id)
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watchCts?.Cancel();
        _watchCts?.Dispose();
    }
}
