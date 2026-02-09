namespace CopilotVoice.Sessions;

public class CopilotSession
{
    public string Id { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string TerminalTitle { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string TerminalApp { get; set; } = string.Empty;
    public bool IsFocused { get; set; }
    public bool IsRegistered { get; set; }
    public string Label
    {
        get
        {
            // For registered sessions, prefer the terminal title (window name)
            if (IsRegistered && !string.IsNullOrEmpty(TerminalTitle)
                && !TerminalTitle.StartsWith("Copilot CLI (PID"))
                return TerminalTitle;

            var basename = string.Empty;
            if (!string.IsNullOrEmpty(WorkingDirectory) && WorkingDirectory != "unknown")
            {
                basename = Path.GetFileName(
                    WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }

            var name = !string.IsNullOrEmpty(basename)
                ? basename
                : !string.IsNullOrEmpty(TerminalTitle) ? TerminalTitle : $"Session {ProcessId}";

            if (!string.IsNullOrEmpty(TerminalApp) && TerminalApp != "Terminal")
                return $"{name} ({TerminalApp})";

            return name;
        }
    }

    public override string ToString() =>
        $"{TerminalApp} — {WorkingDirectory} (PID: {ProcessId})";
}
