namespace CopilotVoice.Messaging;

public class InboundMessage
{
    public string SessionId { get; set; } = string.Empty;
    public string SessionLabel { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public double? DurationHint { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
