namespace CopilotVoice.Bridge;

/// <summary>
/// Adapts ISessionBridge to ICliBridgeClient (Voice.ICliBridgeClient).
/// Allows the FunctionCallHandler to send commands via the bridge.
/// </summary>
internal sealed class BridgeClientAdapter : Voice.ICliBridgeClient
{
    private readonly ISessionBridge _bridge;

    public BridgeClientAdapter(ISessionBridge bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    public Task SendCommandAsync(string prompt, CancellationToken ct = default)
    {
        var sessions = _bridge.ConnectedSessions;
        if (sessions.Count == 0)
            throw new InvalidOperationException("No CLI session connected.");

        // Send to the first connected session (primary session)
        var command = new SendPromptCommand(prompt);
        _bridge.QueueCommand(sessions[0], command);
        return Task.CompletedTask;
    }

    public Voice.SessionBridgeState GetState()
    {
        var sessions = _bridge.ConnectedSessions;
        return new Voice.SessionBridgeState(
            Status: sessions.Count > 0 ? "connected" : "idle",
            CurrentTool: null,
            WorkingDirectory: Environment.CurrentDirectory);
    }
}
