using System.Threading.Channels;

namespace CopilotVoice.Bridge;

/// <summary>
/// Incoming agent message forwarded from the CLI extension.
/// </summary>
public record CliMessage(
    string Type,
    string Content,
    string MessageId,
    long Timestamp,
    bool Truncated = false);

/// <summary>
/// Session lifecycle event from the CLI extension.
/// </summary>
public record CliEvent(
    string Type,
    object? Data,
    long Timestamp);

/// <summary>
/// Command to send a prompt to the CLI extension via SSE.
/// </summary>
public record SendPromptCommand(
    string Prompt,
    object[]? Attachments = null);

/// <summary>
/// Internal state for a connected CLI session.
/// </summary>
public class SessionState
{
    public Channel<SendPromptCommand> CommandChannel { get; }
    public DateTime LastActivity { get; set; }
    public string? SseConnectionId { get; set; }

    public SessionState()
    {
        CommandChannel = Channel.CreateBounded<SendPromptCommand>(
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });
        LastActivity = DateTime.UtcNow;
    }
}
