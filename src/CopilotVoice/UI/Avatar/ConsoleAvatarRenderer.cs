namespace CopilotVoice.UI.Avatar;

/// <summary>
/// Renders avatar frames, speech bubbles, and timer to the console using ANSI escape codes.
/// </summary>
public sealed class ConsoleAvatarRenderer : IDisposable
{
    private readonly AvatarState _state;
    private readonly object _renderLock = new();
    private string[]? _lastFrame;
    private string? _lastBubble;
    private string? _lastTimer;
    private int _avatarStartRow;
    private bool _disposed;

    public ConsoleAvatarRenderer(AvatarState state)
    {
        _state = state;
    }

    /// <summary>
    /// Initialize the renderer — records cursor position for avatar area.
    /// </summary>
    public void Initialize()
    {
        _avatarStartRow = Console.CursorTop;
        RenderCurrentState();
    }

    /// <summary>
    /// Redraw the avatar with the current expression from state.
    /// </summary>
    public void RenderExpression(AvatarExpression expression)
    {
        _state.Expression = expression;
        RenderCurrentState();
    }

    /// <summary>
    /// Show a speech bubble with text and optional session label.
    /// </summary>
    public void ShowSpeechBubble(string text, string? sessionLabel = null)
    {
        _state.SpeechBubbleText = text;
        _state.SpeechBubbleSessionLabel = sessionLabel;
        RenderCurrentState();
    }

    /// <summary>
    /// Clear the speech bubble.
    /// </summary>
    public void ClearSpeechBubble()
    {
        _state.SpeechBubbleText = null;
        _state.SpeechBubbleSessionLabel = null;
        RenderCurrentState();
    }

    /// <summary>
    /// Update the Pomodoro timer display.
    /// </summary>
    public void UpdateTimer(string? phase, TimeSpan? remaining)
    {
        _state.PomodoroPhase = phase;
        _state.PomodoroRemaining = remaining;
        RenderCurrentState();
    }

    private void RenderCurrentState()
    {
        lock (_renderLock)
        {
            if (_disposed) return;

            try
            {
                var theme = _state.GetThemeRenderer();
                var frame = theme.RenderFrame(_state.Expression);
                var bubble = BuildSpeechBubble();
                var timer = BuildTimerLine();

                // Only redraw if something changed
                if (FrameEquals(frame, _lastFrame) && bubble == _lastBubble && timer == _lastTimer)
                    return;

                _lastFrame = frame;
                _lastBubble = bubble;
                _lastTimer = timer;

                // Save cursor, move to avatar area, draw, restore
                var saved = Console.CursorTop;
                var row = _avatarStartRow;

                // Draw avatar frame
                foreach (var line in frame)
                {
                    SetCursorSafe(0, row++);
                    ClearLine();
                    Console.Write($"  {line}");
                }

                // Draw speech bubble (below avatar)
                if (bubble != null)
                {
                    SetCursorSafe(0, row++);
                    ClearLine();
                    Console.Write($"  {bubble}");
                }
                else
                {
                    SetCursorSafe(0, row++);
                    ClearLine();
                }

                // Draw timer
                if (timer != null)
                {
                    SetCursorSafe(0, row++);
                    ClearLine();
                    Console.Write($"  {timer}");
                }
                else
                {
                    SetCursorSafe(0, row++);
                    ClearLine();
                }

                // Blank any leftover lines from previous render
                SetCursorSafe(0, row++);
                ClearLine();

                // Move cursor below the avatar area for log output
                SetCursorSafe(0, row);
            }
            catch
            {
                // Console may not support cursor positioning (redirected output)
            }
        }
    }

    private string? BuildSpeechBubble()
    {
        if (string.IsNullOrEmpty(_state.SpeechBubbleText))
            return null;

        var label = !string.IsNullOrEmpty(_state.SpeechBubbleSessionLabel)
            ? $"[{_state.SpeechBubbleSessionLabel}] "
            : "";

        var text = _state.SpeechBubbleText;
        if (text.Length > 40) text = text[..37] + "...";

        return $"💬 {label}{text}";
    }

    private string? BuildTimerLine()
    {
        if (_state.PomodoroPhase == null || _state.PomodoroRemaining == null)
            return null;

        var icon = _state.PomodoroPhase == "Work" ? "🔨" : "☕";
        var remaining = _state.PomodoroRemaining.Value;
        return $"{icon} {_state.PomodoroPhase}: {remaining.Minutes:D2}:{remaining.Seconds:D2}";
    }

    private static void SetCursorSafe(int left, int top)
    {
        try
        {
            if (top >= 0 && top < Console.BufferHeight && left >= 0)
                Console.SetCursorPosition(left, top);
        }
        catch { /* ignore if terminal doesn't support */ }
    }

    private static void ClearLine()
    {
        Console.Write("\x1b[2K"); // ANSI: clear entire line
    }

    private static bool FrameEquals(string[]? a, string[]? b)
    {
        if (a == null || b == null) return false;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
