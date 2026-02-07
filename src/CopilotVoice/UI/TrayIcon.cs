using CopilotVoice.Sessions;
namespace CopilotVoice.UI;

public enum TrayState { Idle, Recording, Transcribing, Speaking, Error, NoSession, Focus, Break }

public class TrayIcon : IDisposable
{
    private TrayState _state = TrayState.Idle;
    private bool _disposed;

    public event Action? OnSettingsClicked;
    public event Action? OnQuitClicked;
    public event Action<CopilotSession>? OnSessionSelected;
    public event Action? OnLockToggled;

    public void Show() { Console.WriteLine("🎤 Copilot Voice - Ready"); }
    public void Hide() { }
    public void SetState(TrayState state) { _state = state; Console.WriteLine($"Tray: {GetStateIcon(state)} {state}"); }

    private static string GetStateIcon(TrayState s) => s switch
    {
        TrayState.Idle => "🎤", TrayState.Recording => "🔴", TrayState.Transcribing => "⏳",
        TrayState.Speaking => "🔊", TrayState.Error => "⚠️", TrayState.NoSession => "⚠️",
        TrayState.Focus => "🔨", TrayState.Break => "☕", _ => "🎤"
    };

    public void Dispose() { if (!_disposed) { _disposed = true; } }
}
