namespace CopilotVoice.Sessions;

public class CopilotSession
{
    public string Id { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string TerminalTitle { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string TerminalApp { get; set; } = string.Empty;
    public bool IsFocused { get; set; }
    public string Label
    {
        get
        {
            if (string.IsNullOrEmpty(WorkingDirectory) || WorkingDirectory == "unknown")
                return !string.IsNullOrEmpty(TerminalTitle) ? TerminalTitle : $"Session {ProcessId}";

            var basename = Path.GetFileName(
                WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            return string.IsNullOrEmpty(basename) ? TerminalTitle : basename;
        }
    }

    public override string ToString() =>
        $"{TerminalApp} — {WorkingDirectory} (PID: {ProcessId})";
}
