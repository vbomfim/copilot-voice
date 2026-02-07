namespace CopilotVoice.UI.Avatar.Themes;

/// <summary>
/// Emoji-style robot with big eyes, dome cap, and expressive mouth.
/// </summary>
public sealed class RobotTheme : IAvatarTheme
{
    public string Name => "Robot";
    public AvatarTheme ThemeType => AvatarTheme.Robot;

    public string[] RenderFrame(AvatarExpression expression) => expression switch
    {
        AvatarExpression.Normal => new[]
        {
            "  ◠◠◠  ",
            " ┌─────┐",
            " │ ◕ ◕ │",
            " │  ‿  │",
            " └─────┘",
        },
        AvatarExpression.Blink => new[]
        {
            "  ◠◠◠  ",
            " ┌─────┐",
            " │ — — │",
            " │  ‿  │",
            " └─────┘",
        },
        AvatarExpression.HalfBlink => new[]
        {
            "  ◠◠◠  ",
            " ┌─────┐",
            " │ ◔ ◔ │",
            " │  ‿  │",
            " └─────┘",
        },
        AvatarExpression.Yawn => new[]
        {
            "  ◠◠◠  ",
            " ┌─────┐",
            " │ ◔ ◔ │",
            " │  ○  │",
            " └─────┘",
        },
        AvatarExpression.YawnWide => new[]
        {
            "  ◠◠◠  ",
            " ┌─────┐",
            " │ – – │",
            " │  O  │",
            " └─────┘",
        },
        AvatarExpression.Listening => new[]
        {
            "  ◠◠◠  ",
            " ┌─────┐",
            " │ ◉ ◉ │",
            " │  ‿  │",
            " └─────┘",
        },
        AvatarExpression.Thinking => new[]
        {
            "  ◠◠◠  ",
            " ┌─────┐",
            " │ ◑ ◑ │",
            " │  ─  │",
            " └─────┘",
        },
        AvatarExpression.Speaking => new[]
        {
            "  ◠◠◠  ",
            " ┌─────┐",
            " │ ◕ ◕ │",
            " │  ○  │",
            " └─────┘",
        },
        AvatarExpression.Focused => new[]
        {
            "  ◠◠◠  ",
            " ┌─────┐",
            " │ ▸ ◂ │",
            " │  ─  │",
            " └─────┘",
        },
        AvatarExpression.Relaxed => new[]
        {
            "  ◠◠◠  ",
            " ┌─────┐",
            " │ ◡ ◡ │",
            " │  ‿  │",
            " └─────┘",
        },
        AvatarExpression.Sleeping => new[]
        {
            "  ◠◠◠ z",
            " ┌─────┐",
            " │ – – │",
            " │  ‿  │",
            " └─────┘",
            "     zzz",
        },
        _ => RenderFrame(AvatarExpression.Normal),
    };
}
