namespace CopilotVoice.Sessions;

public class CopilotSession
{
    public string Id { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string TerminalTitle { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string TerminalApp { get; set; } = string.Empty;

    public override string ToString() =>
        $"{TerminalApp} — {WorkingDirectory} (PID: {ProcessId})";
}
