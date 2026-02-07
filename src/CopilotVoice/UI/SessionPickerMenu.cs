using CopilotVoice.Sessions;
namespace CopilotVoice.UI;

public class SessionPickerMenu
{
    private readonly SessionDetector _detector;
    public event Action<CopilotSession>? OnSessionSelected;

    public SessionPickerMenu(SessionDetector detector) { _detector = detector; }

    public void DisplaySessions(List<CopilotSession> sessions, CopilotSession? current, SessionTargetMode mode)
    {
        Console.WriteLine($"  Active Sessions:       {(mode == SessionTargetMode.Locked ? "🔒 Locked" : "🔓 Auto")}");
        foreach (var s in sessions)
        {
            var marker = s.Id == current?.Id ? "●" : "○";
            var lockIcon = mode == SessionTargetMode.Locked && s.Id == current?.Id ? "🔒 " : "";
            Console.WriteLine($"  {lockIcon}{marker} {s.TerminalApp} — {s.Label}");
        }
    }

    public void Refresh() { _detector.Refresh(); }
}
