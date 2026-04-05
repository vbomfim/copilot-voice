using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace CopilotVoice.Bridge;

/// <summary>
/// Tracks connected CLI sessions, routes messages, and queues commands.
/// Uses Channel&lt;T&gt; per session for async producer/consumer command delivery.
/// </summary>
public sealed class SessionBridge : ISessionBridge
{
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();

    /// <inheritdoc />
    public event Action<CliMessage>? MessageReceived;

    /// <inheritdoc />
    public event Action<CliEvent>? EventReceived;

    /// <inheritdoc />
    public IReadOnlyList<string> ConnectedSessions =>
        _sessions.Keys.ToList().AsReadOnly();

    /// <inheritdoc />
    public void OnMessageReceived(CliMessage message) =>
        MessageReceived?.Invoke(message);

    /// <inheritdoc />
    public void OnEventReceived(CliEvent evt) =>
        EventReceived?.Invoke(evt);

    /// <inheritdoc />
    public void QueueCommand(string sessionId, SendPromptCommand command)
    {
        var state = GetOrCreateSession(sessionId);
        state.CommandChannel.Writer.TryWrite(command);
        state.LastActivity = DateTime.UtcNow;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SendPromptCommand> GetCommandStream(
        string sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var state = GetOrCreateSession(sessionId);
        state.LastActivity = DateTime.UtcNow;

        await foreach (var command in state.CommandChannel.Reader.ReadAllAsync(ct))
        {
            state.LastActivity = DateTime.UtcNow;
            yield return command;
        }
    }

    /// <inheritdoc />
    public void RemoveSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var state))
        {
            state.CommandChannel.Writer.TryComplete();
        }
    }

    private SessionState GetOrCreateSession(string sessionId)
    {
        if (_sessions.Count >= BridgeServer.MaxSessions && !_sessions.ContainsKey(sessionId))
            throw new InvalidOperationException($"Maximum session limit ({BridgeServer.MaxSessions}) reached");

        return _sessions.GetOrAdd(sessionId, _ => new SessionState());
    }
}
