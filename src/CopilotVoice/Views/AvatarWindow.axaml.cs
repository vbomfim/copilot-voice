using Avalonia.Controls;
using Avalonia.Threading;
using CopilotVoice.UI.Avatar;

namespace CopilotVoice.Views;

public partial class AvatarWindow : Window
{
    private AppServices? _services;

    public AvatarWindow()
    {
        InitializeComponent();

        // Show initial frame
        AvatarPixel.SetPixelSize(14);
        AvatarPixel.SetFrame(PixelAvatarData.GetFrame(AvatarExpression.Normal));

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

        services.Animator.OnExpressionChanged += expr =>
            Dispatcher.UIThread.Post(() => UpdateFace(expr));

        services.OnStateChanged += state =>
            Dispatcher.UIThread.Post(() => UpdateStatus(state));

        services.OnSpeechBubble += (text, label) =>
            Dispatcher.UIThread.Post(() =>
            {
                SpeechBubble.Text = string.IsNullOrEmpty(text) ? "" : $"\U0001f4ac {text}";
                if (!string.IsNullOrEmpty(label))
                    SessionLabel.Text = $"\U0001f4c2 {label}";
            });

        services.OnTargetSession += label =>
            Dispatcher.UIThread.Post(() =>
                SessionLabel.Text = $"\U0001f4c2 {label ?? "no session"}");

        services.OnTimerTick += (phase, remaining) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (phase == null)
                    TimerLabel.Text = "";
                else
                {
                    var icon = phase == "Work" ? "\U0001f528" : "\u2615";
                    TimerLabel.Text = $"{icon} {phase} {remaining:mm\\:ss}";
                }
            });

        services.OnLog += msg =>
            Dispatcher.UIThread.Post(() => System.Console.WriteLine($"[UI] {msg}"));

        HotkeyLabel.Text = $"\u2328\ufe0f {services.Config.Hotkey}";
    }

    private void UpdateFace(AvatarExpression expression)
    {
        AvatarPixel.SetFrame(PixelAvatarData.GetFrame(expression));
    }

    private void UpdateStatus(string state)
    {
        StatusBadge.Text = state switch
        {
            "Recording" => "\U0001f534 Recording...",
            "Transcribing" => "\u23f3 Transcribing...",
            "Speaking" => "\U0001f50a Speaking",
            "Error" => "\u26a0\ufe0f Error",
            _ => "\u25cf Ready"
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
}
