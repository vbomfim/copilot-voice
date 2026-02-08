namespace CopilotVoice.UI.Avatar.Themes;

/// <summary>
/// Robot face using Unicode box-drawing and special characters.
/// Designed for monospace font rendering in Avalonia.
/// </summary>
public sealed class RobotTheme : IAvatarTheme
{
    public string Name => "Robot";
    public AvatarTheme ThemeType => AvatarTheme.Robot;

    public string[] RenderFrame(AvatarExpression expression) => expression switch
    {
        AvatarExpression.Normal => new[]
        {
            @"       .----.       ",
            @"      |  []  |      ",
            @"    .---------.    ",
            @"   /           \   ",
            @"  |   O     O   |  ",
            @"  |             |  ",
            @"  |    \___/    |  ",
            @"   \           /   ",
            @"    '---------'    ",
            @"      ||| |||      ",
        },
        AvatarExpression.Blink => new[]
        {
            @"       .----.       ",
            @"      |  []  |      ",
            @"    .---------.    ",
            @"   /           \   ",
            @"  |   -     -   |  ",
            @"  |             |  ",
            @"  |    \___/    |  ",
            @"   \           /   ",
            @"    '---------'    ",
            @"      ||| |||      ",
        },
        AvatarExpression.HalfBlink => new[]
        {
            @"       .----.       ",
            @"      |  []  |      ",
            @"    .---------.    ",
            @"   /           \   ",
            @"  |   o     o   |  ",
            @"  |             |  ",
            @"  |    \___/    |  ",
            @"   \           /   ",
            @"    '---------'    ",
            @"      ||| |||      ",
        },
        AvatarExpression.Yawn => new[]
        {
            @"       .----.       ",
            @"      |  []  |      ",
            @"    .---------.    ",
            @"   /           \   ",
            @"  |   o     o   |  ",
            @"  |             |  ",
            @"  |      O      |  ",
            @"   \           /   ",
            @"    '---------'    ",
            @"      ||| |||      ",
        },
        AvatarExpression.YawnWide => new[]
        {
            @"       .----.       ",
            @"      |  []  |      ",
            @"    .---------.    ",
            @"   /           \   ",
            @"  |   -     -   |  ",
            @"  |             |  ",
            @"  |     (O)     |  ",
            @"   \           /   ",
            @"    '---------'    ",
            @"      ||| |||      ",
        },
        AvatarExpression.Listening => new[]
        {
            @"       .----.       ",
            @"      | [!!] |      ",
            @"    .---------.    ",
            @"   /           \   ",
            @"  |   @     @   |  ",
            @"  |             |  ",
            @"  |      o      |  ",
            @"   \           /   ",
            @"    '---------'    ",
            @"      ||| |||      ",
        },
        AvatarExpression.Thinking => new[]
        {
            @"       .----.       ",
            @"      |  []  |      ",
            @"    .---------.    ",
            @"   /           \   ",
            @"  |   o     O   |  ",
            @"  |             |  ",
            @"  |      ~      |  ",
            @"   \           /   ",
            @"    '---------'    ",
            @"      ||| |||      ",
        },
        AvatarExpression.Speaking => new[]
        {
            @"       .----.       ",
            @"      |  []  |      ",
            @"    .---------.    ",
            @"   /           \   ",
            @"  |   O     O   |  ",
            @"  |             |  ",
            @"  |    [===]    |  ",
            @"   \           /   ",
            @"    '---------'    ",
            @"      ||| |||      ",
        },
        AvatarExpression.Focused => new[]
        {
            @"       .----.       ",
            @"      | [**] |      ",
            @"    .---------.    ",
            @"   /           \   ",
            @"  |   >     <   |  ",
            @"  |             |  ",
            @"  |    .---.    |  ",
            @"   \           /   ",
            @"    '---------'    ",
            @"      ||| |||      ",
        },
        AvatarExpression.Relaxed => new[]
        {
            @"       .----.       ",
            @"      |  []  |      ",
            @"    .---------.    ",
            @"   /           \   ",
            @"  |   ^     ^   |  ",
            @"  |             |  ",
            @"  |    \___/    |  ",
            @"   \           /   ",
            @"    '---------'    ",
            @"      ||| |||      ",
        },
        AvatarExpression.Sleeping => new[]
        {
            @"       .----.    z  ",
            @"      |  []  |   z ",
            @"    .---------.    ",
            @"   /           \   ",
            @"  |   -     -   |  ",
            @"  |             |  ",
            @"  |    \___/    |  ",
            @"   \           /   ",
            @"    '---------'    ",
            @"      ||| |||   zz ",
        },
        _ => RenderFrame(AvatarExpression.Normal),
    };
}
