using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CopilotVoice.UI.Avatar;

namespace CopilotVoice.Views;

public partial class AvatarWindow : Window
{
    private AppServices? _services;
    private bool _isDragging;
    private PixelPoint _dragStart;

    private bool _firstOpen = true;
    private PixelPoint _savedPosition;

    public event Action<bool>? OnTopmostChanged;
    public event Action<bool>? OnVisibilityChanged;

    public AvatarWindow()
    {
        InitializeComponent();

        // Show initial frame
        AvatarPixel.SetPixelSize(14);
        AvatarPixel.SetFrame(PixelAvatarData.GetFrame(AvatarExpression.Normal));

        // Position bottom-right on first open only
        Opened += (_, _) =>
        {
            if (_firstOpen)
            {
                _firstOpen = false;
                Dispatcher.UIThread.Post(() =>
                {
                    ResetPosition();
                }, Avalonia.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                Position = _savedPosition;
            }
        };

        // Drag to move (since no title bar)
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _isDragging = true;
                _dragStart = e.GetPosition(this) is { } p
                    ? new PixelPoint((int)p.X, (int)p.Y) : default;
                e.Pointer.Capture(this);
            }
        };
        PointerMoved += (_, e) =>
        {
            if (_isDragging)
            {
                var pos = e.GetPosition(this);
                var delta = new PixelPoint((int)pos.X, (int)pos.Y) - _dragStart;
                Position = new PixelPoint(Position.X + delta.X, Position.Y + delta.Y);
            }
        };
        PointerReleased += (_, e) =>
        {
            if (_isDragging)
            {
                _isDragging = false;
                e.Pointer.Capture(null);
            }
        };
    }

    public void SetServices(AppServices services)
    {
        _services = services;

        services.Animator.OnExpressionChanged += expr =>
            Dispatcher.UIThread.Post(() => { try { UpdateFace(expr); } catch (Exception ex) { Console.WriteLine($"[UI] Face error: {ex.Message}"); } });

        services.OnStateChanged += state =>
            Dispatcher.UIThread.Post(() => { try { UpdateStatus(state); } catch (Exception ex) { Console.WriteLine($"[UI] Status error: {ex.Message}"); } });

        services.OnSpeechBubble += (text, label) =>
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(text))
                    {
                        BalloonPanel.IsVisible = false;
                        SpeechBubble.Text = "";
                    }
                    else
                    {
                        SpeechBubble.Text = text;
                        BalloonPanel.IsVisible = true;
                    }
                    if (!string.IsNullOrEmpty(label))
                        SessionLabel.Text = $"\U0001f4c2 {label}";
                }
                catch (Exception ex) { Console.WriteLine($"[UI] Bubble error: {ex.Message}"); }
            });

        services.OnTargetSession += label =>
            Dispatcher.UIThread.Post(() =>
            {
                try { SessionLabel.Text = $"\U0001f4c2 {label ?? "no session"}"; }
                catch (Exception ex) { Console.WriteLine($"[UI] Session error: {ex.Message}"); }
            });

        services.OnTranscriptionUpdate += text =>
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(text))
                    {
                        TranscriptionPanel.IsVisible = false;
                        TranscriptionText.Text = "";
                    }
                    else
                    {
                        TranscriptionText.Text = text;
                        TranscriptionPanel.IsVisible = true;
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[UI] Transcription error: {ex.Message}"); }
            });

        services.OnTimerTick += (phase, remaining) =>
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (phase == null)
                        TimerLabel.Text = "";
                    else
                    {
                        var icon = phase == "Work" ? "\U0001f528" : "\u2615";
                        TimerLabel.Text = $"{icon} {phase} {remaining:mm\\:ss}";
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[UI] Timer error: {ex.Message}"); }
            });

        services.OnLog += msg =>
            Dispatcher.UIThread.Post(() => Console.WriteLine($"[UI] {msg}"));

        services.OnWindowControl += async (action, x, y, position) =>
        {
            var tcs = new TaskCompletionSource<string>();
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var result = action switch
                    {
                        "show" => DoShow(),
                        "hide" => DoHide(),
                        "toggle" => IsVisible ? DoHide() : DoShow(),
                        "topmost_on" => DoTopmost(true),
                        "topmost_off" => DoTopmost(false),
                        "move" => DoMove(x, y, position),
                        _ => $"Unknown action: {action}"
                    };
                    tcs.TrySetResult(result);
                }
                catch (Exception ex) { tcs.TrySetResult($"Error: {ex.Message}"); }
            });
            return await tcs.Task;
        };

        HotkeyLabel.Text = $"\u2328\ufe0f {services.Config.Hotkey}";
    }

    private string DoShow() { Show(); OnVisibilityChanged?.Invoke(true); return "Window shown"; }
    private string DoHide() { _savedPosition = Position; Hide(); OnVisibilityChanged?.Invoke(false); return "Window hidden"; }
    public void SetTopmost(bool on)
    {
        Topmost = on;
        PinButton.Content = on ? "📌" : "📍";
        OnTopmostChanged?.Invoke(on);
    }
    private string DoTopmost(bool on)
    {
        SetTopmost(on);
        return $"Topmost: {on}";
    }
    private void OnPinClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetTopmost(!Topmost);
    }
    public void ResetPosition()
    {
        var screen = Screens.Primary;
        if (screen != null)
        {
            var workArea = screen.WorkingArea;
            var w = Bounds.Width > 0 ? Bounds.Width : 280;
            // Top-right, near macOS tray/menu bar
            Position = new PixelPoint(
                (int)(workArea.Right - w - 20),
                workArea.Y + 10);
        }
    }
    private void OnHideClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _savedPosition = Position;
        Hide();
        OnVisibilityChanged?.Invoke(false);
    }
    private string DoMove(int? x, int? y, string? position)
    {
        if (position != null)
        {
            var screen = Screens.Primary;
            if (screen == null) return "No screen";
            var wa = screen.WorkingArea;
            var w = Bounds.Width > 0 ? Bounds.Width : 280;
            var h = Bounds.Height > 0 ? Bounds.Height : 300;
            var (px, py) = position switch
            {
                "top-left" => (wa.X + 20, wa.Y + 20),
                "top-right" => ((int)(wa.Right - w - 20), wa.Y + 20),
                "bottom-left" => (wa.X + 20, (int)(wa.Bottom - h - 20)),
                "bottom-right" => ((int)(wa.Right - w - 20), (int)(wa.Bottom - h - 20)),
                "center" => ((int)(wa.X + (wa.Width - w) / 2), (int)(wa.Y + (wa.Height - h) / 2)),
                _ => (-1, -1)
            };
            if (px < 0) return $"Unknown position: {position}";
            Position = new PixelPoint(px, py);
            return $"Moved to {position} ({px},{py})";
        }
        if (x != null && y != null)
        {
            Position = new PixelPoint(x.Value, y.Value);
            return $"Moved to ({x},{y})";
        }
        return "Specify x,y or position";
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
