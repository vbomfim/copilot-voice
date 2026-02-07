using CopilotVoice.UI.Avatar.Themes;

namespace CopilotVoice.UI.Avatar;

/// <summary>
/// Centralized state for the avatar widget: current expression, theme,
/// speech bubble content, and Pomodoro timer.
/// </summary>
public sealed class AvatarState
{
    public AvatarExpression Expression { get; set; } = AvatarExpression.Normal;
    public AvatarTheme Theme { get; set; } = AvatarTheme.Robot;

    public string? SpeechBubbleText { get; set; }
    public string? SpeechBubbleSessionLabel { get; set; }

    public string? PomodoroPhase { get; set; }
    public TimeSpan? PomodoroRemaining { get; set; }

    /// <summary>
    /// Returns the <see cref="IAvatarTheme"/> renderer for the current <see cref="Theme"/>.
    /// </summary>
    public IAvatarTheme GetThemeRenderer() => Theme switch
    {
        AvatarTheme.Robot    => new RobotTheme(),
        AvatarTheme.Waveform => new WaveformTheme(),
        AvatarTheme.Symbols  => new SymbolsTheme(),
        _ => new RobotTheme(),
    };
}
