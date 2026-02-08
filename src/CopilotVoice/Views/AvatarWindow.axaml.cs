using Avalonia.Controls;
using Avalonia.Threading;
using CopilotVoice.UI.Avatar;

namespace CopilotVoice.Views;

public partial class AvatarWindow : Window
{
    private AppServices? _services;
    private IAvatarTheme? _theme;

    public AvatarWindow()
    {
        InitializeComponent();

        // Position bottom-right of screen
        Opened += (_, _) =>
        {
            var screen = Screens.Primary;
            if (screen != null)
            {
                var workArea = screen.WorkingArea;
                Position = new Avalonia.PixelPoint(
                    (int)(workArea.Right - Width - 20),
                    (int)(workArea.Bottom - Height - 20));
            }
        };
    }

    public void SetServices(AppServices services)
    {
        _services = services;
        _theme = services.AvatarState.GetThemeRenderer();

        services.Animator.OnExpressionChanged += expr =>
            Dispatcher.UIThread.Post(() => UpdateFace(expr));

        services.OnStateChanged += state =>
            Dispatcher.UIThread.Post(() => UpdateStatus(state));

        services.OnSpeechBubble += (text, label) =>
            Dispatcher.UIThread.Post(() =>
            {
                SpeechBubble.Text = string.IsNullOrEmpty(text) ? "" : $"💬 {text}";
                if (!string.IsNullOrEmpty(label))
                    SessionLabel.Text = $"📂 {label}";
            });

        services.OnTargetSession += label =>
            Dispatcher.UIThread.Post(() =>
                SessionLabel.Text = $"📂 {label ?? "no session"}");

        services.OnTimerTick += (phase, remaining) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (phase == null)
                    TimerLabel.Text = "";
                else
                {
                    var icon = phase == "Work" ? "🔨" : "☕";
                    TimerLabel.Text = $"{icon} {phase} {remaining:mm\\:ss}";
                }
            });

        services.OnLog += msg =>
            Dispatcher.UIThread.Post(() => AppendLog(msg));

        // Show hotkey
        HotkeyLabel.Text = $"⌨️ {services.Config.Hotkey}";
    }

    private void UpdateFace(AvatarExpression expression)
    {
        if (_theme == null) return;
        var lines = _theme.RenderFrame(expression);
        AvatarFace.Text = string.Join("\n", lines);
    }

    private void UpdateStatus(string state)
    {
        StatusBadge.Text = state switch
        {
            "Recording" => "🔴 Recording...",
            "Transcribing" => "⏳ Transcribing...",
            "Speaking" => "🔊 Speaking",
            "Error" => "⚠️ Error",
            _ => "● Ready"
        };

        StatusBadge.Foreground = state switch
        {
            "Recording" => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F38BA8")),
            "Transcribing" => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F9E2AF")),
            "Speaking" => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#89B4FA")),
            "Error" => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FAB387")),
            _ => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#A6E3A1"))
        };
    }

    private void AppendLog(string msg)
    {
        // Log output goes to console only — no visible log area in the UI
        System.Console.WriteLine($"[UI] {msg}");
    }
}
