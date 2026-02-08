namespace CopilotVoice.UI.Avatar.Themes;

/// <summary>
/// Robot face using pure ASCII for reliable monospace rendering.
/// Every line is exactly 21 characters wide after C# escape processing.
/// </summary>
public sealed class RobotTheme : IAvatarTheme
{
    public string Name => "Robot";
    public AvatarTheme ThemeType => AvatarTheme.Robot;

    //                    123456789012345678901
    public string[] RenderFrame(AvatarExpression expression) => expression switch
    {
        AvatarExpression.Normal => new[]
        { //               123456789012345678901
            "     .----.          ",
            "    | [==] |         ",
            "  .-----------.     ",
            " /             \\    ",
            "|   O       O   |   ",
            "|               |   ",
            "|    \\_____/    |   ",
            " \\             /    ",
            "  '-----------'     ",
            "    ||| | |||       ",
        },
        AvatarExpression.Blink => new[]
        {
            "     .----.          ",
            "    | [==] |         ",
            "  .-----------.     ",
            " /             \\    ",
            "|   -       -   |   ",
            "|               |   ",
            "|    \\_____/    |   ",
            " \\             /    ",
            "  '-----------'     ",
            "    ||| | |||       ",
        },
        AvatarExpression.HalfBlink => new[]
        {
            "     .----.          ",
            "    | [==] |         ",
            "  .-----------.     ",
            " /             \\    ",
            "|   o       o   |   ",
            "|               |   ",
            "|    \\_____/    |   ",
            " \\             /    ",
            "  '-----------'     ",
            "    ||| | |||       ",
        },
        AvatarExpression.Yawn => new[]
        {
            "     .----.          ",
            "    | [==] |         ",
            "  .-----------.     ",
            " /             \\    ",
            "|   o       o   |   ",
            "|               |   ",
            "|       O       |   ",
            " \\             /    ",
            "  '-----------'     ",
            "    ||| | |||       ",
        },
        AvatarExpression.YawnWide => new[]
        {
            "     .----.          ",
            "    | [==] |         ",
            "  .-----------.     ",
            " /             \\    ",
            "|   -       -   |   ",
            "|               |   ",
            "|      (O)      |   ",
            " \\             /    ",
            "  '-----------'     ",
            "    ||| | |||       ",
        },
        AvatarExpression.Listening => new[]
        {
            "     .----.          ",
            "    | [!!] |         ",
            "  .-----------.     ",
            " /             \\    ",
            "|   @       @   |   ",
            "|               |   ",
            "|       o       |   ",
            " \\             /    ",
            "  '-----------'     ",
            "    ||| | |||       ",
        },
        AvatarExpression.Thinking => new[]
        {
            "     .----.          ",
            "    | [==] |         ",
            "  .-----------.     ",
            " /             \\    ",
            "|   o       O   |   ",
            "|               |   ",
            "|       ~       |   ",
            " \\             /    ",
            "  '-----------'     ",
            "    ||| | |||       ",
        },
        AvatarExpression.Speaking => new[]
        {
            "     .----.          ",
            "    | [==] |         ",
            "  .-----------.     ",
            " /             \\    ",
            "|   O       O   |   ",
            "|               |   ",
            "|    [=====]    |   ",
            " \\             /    ",
            "  '-----------'     ",
            "    ||| | |||       ",
        },
        AvatarExpression.Focused => new[]
        {
            "     .----.          ",
            "    | [**] |         ",
            "  .-----------.     ",
            " /             \\    ",
            "|   >       <   |   ",
            "|               |   ",
            "|     .---.     |   ",
            " \\             /    ",
            "  '-----------'     ",
            "    ||| | |||       ",
        },
        AvatarExpression.Relaxed => new[]
        {
            "     .----.          ",
            "    | [==] |         ",
            "  .-----------.     ",
            " /             \\    ",
            "|   ^       ^   |   ",
            "|               |   ",
            "|    \\_____/    |   ",
            " \\             /    ",
            "  '-----------'     ",
            "    ||| | |||       ",
        },
        AvatarExpression.Sleeping => new[]
        {
            "     .----.        z ",
            "    | [==] |      z  ",
            "  .-----------.     ",
            " /             \\    ",
            "|   -       -   |   ",
            "|               |   ",
            "|    \\_____/    |   ",
            " \\             /    ",
            "  '-----------'     ",
            "    ||| | |||    zz  ",
        },
        _ => RenderFrame(AvatarExpression.Normal),
    };
}
