namespace CopilotVoice.Sessions;

public class CopilotSession
{
    public string Id { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string TerminalTitle { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string TerminalApp { get; set; } = string.Empty;
    public bool IsFocused { get; set; }
    public string Label => string.IsNullOrEmpty(WorkingDirectory) || WorkingDirectory == "unknown"
        ? $"Session {ProcessId}"
        : Path.GetFileName(WorkingDirectory);

    public override string ToString() =>
        $"{TerminalApp} — {WorkingDirectory} (PID: {ProcessId})";
}
