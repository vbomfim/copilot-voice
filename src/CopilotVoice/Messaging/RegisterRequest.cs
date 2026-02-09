namespace CopilotVoice.Messaging;

public class RegisterRequest
{
    public int Pid { get; set; }
    public string WorkingDirectory { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string TerminalApp { get; set; } = string.Empty;
}
