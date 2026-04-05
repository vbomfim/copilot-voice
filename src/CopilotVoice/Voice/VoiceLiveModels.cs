namespace CopilotVoice.Voice;

/// <summary>
/// Represents a function call request from the Voice Live API.
/// Fired when the model wants to invoke a tool.
/// </summary>
public record FunctionCall(string CallId, string Name, string Arguments);

/// <summary>
/// Session update configuration sent to the Voice Live API on connect or reconfigure.
/// Maps to the "session" sub-object of a "session.update" event.
/// </summary>
public record SessionUpdate(
    IReadOnlyList<string> Modalities,
    string Voice,
    string Instructions,
    string InputAudioFormat = "pcm16",
    string OutputAudioFormat = "pcm16",
    IReadOnlyList<ToolDefinition>? Tools = null,
    string ToolChoice = "auto"
);

/// <summary>
/// Definition of a function tool exposed to the Voice Live API model.
/// </summary>
public record ToolDefinition(
    string Name,
    string Description,
    string ParametersJson
);

/// <summary>
/// Minimal bridge interface to the Copilot CLI session.
/// Implemented by the CLI Integration Bridge (#61). Nullable dependency — handlers
/// work standalone with stub responses when no bridge is wired.
/// Named ICliBridgeClient (not ISessionBridge) to avoid collision with Bridge.ISessionBridge.
/// </summary>
public interface ICliBridgeClient
{
    /// <summary>Queue a command/prompt to the active Copilot CLI session.</summary>
    Task SendCommandAsync(string prompt, CancellationToken ct = default);

    /// <summary>Return the current state of the CLI session.</summary>
    SessionBridgeState GetState();
}

/// <summary>
/// Snapshot of the CLI session state returned by ICliBridgeClient.
/// </summary>
public record SessionBridgeState(
    string Status,
    string? CurrentTool,
    string? WorkingDirectory
);
