namespace CopilotVoice.Sessions;

public class SessionManager : IDisposable
{
    private readonly SessionDetector _detector;
    private readonly List<CopilotSession> _registeredSessions = new();
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

    public CopilotSession RegisterSession(Messaging.RegisterRequest request)
    {
        // Remove existing registration for same PID or same working directory
        _registeredSessions.RemoveAll(s =>
            s.ProcessId == request.Pid ||
            (!string.IsNullOrEmpty(request.WorkingDirectory) &&
             s.WorkingDirectory == request.WorkingDirectory));

        var cwd = !string.IsNullOrEmpty(request.WorkingDirectory)
            ? request.WorkingDirectory
            : Environment.CurrentDirectory;

        var terminalApp = !string.IsNullOrEmpty(request.TerminalApp)
            ? request.TerminalApp
            : DetectTerminalApp(request.Pid);

        var session = new CopilotSession
        {
            Id = $"registered-{request.Pid}",
            ProcessId = request.Pid,
            WorkingDirectory = cwd,
            TerminalApp = terminalApp,
            TerminalTitle = !string.IsNullOrEmpty(request.Label) ? request.Label : $"Copilot CLI (PID {request.Pid})",
            IsRegistered = true
        };

        _registeredSessions.Add(session);
        return session;
    }

    /// <summary>
    /// Walk up the process tree from a PID to find the terminal application.
    /// </summary>
    private static string DetectTerminalApp(int pid)
    {
        var knownTerminals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ghostty"] = "Ghostty",
            ["iterm2"] = "iTerm2",
            ["warp"] = "Warp",
            ["terminal"] = "Terminal",
            ["alacritty"] = "Alacritty",
            ["kitty"] = "kitty",
            ["hyper"] = "Hyper",
            ["tabby"] = "Tabby",
        };

        try
        {
            var current = pid;
            for (var i = 0; i < 10; i++)
            {
                var psi = new System.Diagnostics.ProcessStartInfo("ps", $"-o ppid=,comm= -p {current}")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                var output = proc?.StandardOutput.ReadToEnd().Trim() ?? "";
                proc?.WaitForExit();

                if (string.IsNullOrEmpty(output)) break;

                var parts = output.Split((char[])null!, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) break;

                if (!int.TryParse(parts[0], out var ppid) || ppid <= 1) break;

                var comm = Path.GetFileNameWithoutExtension(parts[1].Trim());
                foreach (var (key, appName) in knownTerminals)
                {
                    if (comm.Contains(key, StringComparison.OrdinalIgnoreCase))
                        return appName;
                }

                current = ppid;
            }
        }
        catch
        {
            // Best effort — fall back to Terminal
        }

        return "Terminal";
    }

    /// <summary>
    /// Returns registered sessions first, then auto-detected sessions (excluding duplicates).
    /// </summary>
    public List<CopilotSession> GetAllSessions()
    {
        var detected = _detector.GetCachedSessions();
        var registeredPids = new HashSet<int>(_registeredSessions.Select(s => s.ProcessId));
        var combined = new List<CopilotSession>(_registeredSessions);
        combined.AddRange(detected.Where(s => !registeredPids.Contains(s.ProcessId)));
        return combined;
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
                    // Registered sessions are always considered alive — skip liveness check
                    if (!_lockedSession.IsRegistered)
                    {
                        var sessions = _detector.GetCachedSessions();
                        if (!sessions.Any(s => s.Id == _lockedSession.Id))
                        {
                            Unlock();
                            OnTargetChanged?.Invoke(null);
                        }
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
