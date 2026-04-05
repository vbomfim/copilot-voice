namespace CopilotVoice.Bridge;

/// <summary>
/// Tracks connected CLI sessions, routes messages, and queues commands.
/// </summary>
public interface ISessionBridge
{
    /// <summary>Dispatch an inbound agent message from the CLI extension.</summary>
    void OnMessageReceived(CliMessage message);

    /// <summary>Dispatch an inbound session lifecycle event.</summary>
    void OnEventReceived(CliEvent evt);

    /// <summary>Queue a command to be delivered to a specific session via SSE.</summary>
    void QueueCommand(string sessionId, SendPromptCommand command);

    /// <summary>Async stream of commands for a specific session (consumed by SSE endpoint).</summary>
    IAsyncEnumerable<SendPromptCommand> GetCommandStream(string sessionId, CancellationToken ct);

    /// <summary>List of currently connected session IDs.</summary>
    IReadOnlyList<string> ConnectedSessions { get; }

    /// <summary>Remove a session (e.g., on SSE disconnect).</summary>
    void RemoveSession(string sessionId);

    /// <summary>Fired when a CLI message is received.</summary>
    event Action<CliMessage>? MessageReceived;

    /// <summary>Fired when a CLI event is received.</summary>
    event Action<CliEvent>? EventReceived;
}
