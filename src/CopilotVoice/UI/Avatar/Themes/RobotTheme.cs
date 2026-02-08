namespace CopilotVoice.UI.Avatar.Themes;

/// <summary>
/// Robot face using pure ASCII box-drawing for reliable monospace rendering.
/// All characters are fixed-width ASCII to avoid alignment issues in Avalonia.
/// </summary>
public sealed class RobotTheme : IAvatarTheme
{
    public string Name => "Robot";
    public AvatarTheme ThemeType => AvatarTheme.Robot;

    public string[] RenderFrame(AvatarExpression expression) => expression switch
    {
        AvatarExpression.Normal => new[]
        {
            "    ___    ",
            "   |___|   ",
            "  /     \\  ",
            " | O   O | ",
            " |   _   | ",
            " |  \\_/  | ",
            "  \\_____/  ",
            "   || ||   ",
        },
        AvatarExpression.Blink => new[]
        {
            "    ___    ",
            "   |___|   ",
            "  /     \\  ",
            " | -   - | ",
            " |   _   | ",
            " |  \\_/  | ",
            "  \\_____/  ",
            "   || ||   ",
        },
        AvatarExpression.HalfBlink => new[]
        {
            "    ___    ",
            "   |___|   ",
            "  /     \\  ",
            " | o   o | ",
            " |   _   | ",
            " |  \\_/  | ",
            "  \\_____/  ",
            "   || ||   ",
        },
        AvatarExpression.Yawn => new[]
        {
            "    ___    ",
            "   |___|   ",
            "  /     \\  ",
            " | o   o | ",
            " |       | ",
            " |   O   | ",
            "  \\_____/  ",
            "   || ||   ",
        },
        AvatarExpression.YawnWide => new[]
        {
            "    ___    ",
            "   |___|   ",
            "  /     \\  ",
            " | -   - | ",
            " |       | ",
            " |  (O)  | ",
            "  \\_____/  ",
            "   || ||   ",
        },
        AvatarExpression.Listening => new[]
        {
            "    ___    ",
            "   |___|   ",
            "  /     \\  ",
            " | @   @ | ",
            " |   _   | ",
            " |  \\_/  | ",
            "  \\_____/  ",
            "   || ||   ",
        },
        AvatarExpression.Thinking => new[]
        {
            "    ___    ",
            "   |___|   ",
            "  /     \\  ",
            " | o   O | ",
            " |       | ",
            " |   ~   | ",
            "  \\_____/  ",
            "   || ||   ",
        },
        AvatarExpression.Speaking => new[]
        {
            "    ___    ",
            "   |___|   ",
            "  /     \\  ",
            " | O   O | ",
            " |       | ",
            " |  ___  | ",
            "  \\_____/  ",
            "   || ||   ",
        },
        AvatarExpression.Focused => new[]
        {
            "    ___    ",
            "   |___|   ",
            "  /     \\  ",
            " | >   < | ",
            " |       | ",
            " |  ---  | ",
            "  \\_____/  ",
            "   || ||   ",
        },
        AvatarExpression.Relaxed => new[]
        {
            "    ___    ",
            "   |___|   ",
            "  /     \\  ",
            " | ^   ^ | ",
            " |   _   | ",
            " |  \\_/  | ",
            "  \\_____/  ",
            "   || ||   ",
        },
        AvatarExpression.Sleeping => new[]
        {
            "    ___   z",
            "   |___|   ",
            "  /     \\  ",
            " | -   - | ",
            " |   _   | ",
            " |  \\_/  | ",
            "  \\_____/ z",
            "   || || zz",
        },
        _ => RenderFrame(AvatarExpression.Normal),
    };
}
