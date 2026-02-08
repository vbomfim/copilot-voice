namespace CopilotVoice.UI.Avatar;

/// <summary>
/// Pixel art data for the robot avatar.
/// Each expression is a 2D grid of color codes:
///   '.' = transparent (background)
///   'B' = light blue (face)
///   'b' = cyan highlight
///   'P' = purple (hat, sides)
///   'D' = dark (eyes, mouth area)
///   'G' = green (mouth teeth)
///   'W' = white (eye shine)
/// </summary>
public static class PixelAvatarData
{
    public const string ColorTransparent = "#00000000";
    public const string ColorBlue = "#7EC8E3";
    public const string ColorCyan = "#89CFF0";
    public const string ColorPurple = "#9B72CF";
    public const string ColorDark = "#1A1A2E";
    public const string ColorGreen = "#A6E3A1";
    public const string ColorWhite = "#FFFFFF";
    public const string ColorRed = "#F38BA8";
    public const string ColorYellow = "#F9E2AF";
    public const string ColorOrange = "#FAB387";

    public static string GetColor(char code) => code switch
    {
        'B' => ColorBlue,
        'b' => ColorCyan,
        'P' => ColorPurple,
        'D' => ColorDark,
        'G' => ColorGreen,
        'W' => ColorWhite,
        'R' => ColorRed,
        'Y' => ColorYellow,
        'O' => ColorOrange,
        _ => ColorTransparent,
    };

    // 18 wide x 18 tall — Copilot CLI robot face
    public static string[] GetFrame(AvatarExpression expression) => expression switch
    {
        AvatarExpression.Normal => new[]
        {
            "......PPPPPP......",
            "....PPPPPPPPPP....",
            "...PPPPPPPPPPPP...",
            "..BBBBBBBBBBBBBB..",
            "..BDDDDBBDDDDBB...",
            "..BDWDDBBDWDDBB...",
            "..BDDDDBBDDDDBB...",
            "..BBBBBBBBBBBBBB..",
            "..BBBBBBBBBBBBBB..",
            ".PBBBBBBBBBBBBBBP.",
            ".PBBDDDDDDDDDDBBP.",
            ".PBBDGGDDDDGGDBBP.",
            ".PBBDDDDDDDDDDBBP.",
            "..BBBBBBBBBBBBBB..",
            "...PPPPPPPPPPPP...",
            "....PPPPPPPPPP....",
            ".....PPPPPPPP.....",
            "..................",
        },
        AvatarExpression.Blink => new[]
        {
            "......PPPPPP......",
            "....PPPPPPPPPP....",
            "...PPPPPPPPPPPP...",
            "..BBBBBBBBBBBBBB..",
            "..BBBBBBBBBBBBBB..",
            "..BDDDDBBDDDDBB...",
            "..BBBBBBBBBBBBBB..",
            "..BBBBBBBBBBBBBB..",
            "..BBBBBBBBBBBBBB..",
            ".PBBBBBBBBBBBBBBP.",
            ".PBBDDDDDDDDDDBBP.",
            ".PBBDGGDDDDGGDBBP.",
            ".PBBDDDDDDDDDDBBP.",
            "..BBBBBBBBBBBBBB..",
            "...PPPPPPPPPPPP...",
            "....PPPPPPPPPP....",
            ".....PPPPPPPP.....",
            "..................",
        },
        AvatarExpression.HalfBlink => new[]
        {
            "......PPPPPP......",
            "....PPPPPPPPPP....",
            "...PPPPPPPPPPPP...",
            "..BBBBBBBBBBBBBB..",
            "..BBDDDBBBBDDDBB..",
            "..BDDDDBBDDDDBB...",
            "..BDDDDBBDDDDBB...",
            "..BBBBBBBBBBBBBB..",
            "..BBBBBBBBBBBBBB..",
            ".PBBBBBBBBBBBBBBP.",
            ".PBBDDDDDDDDDDBBP.",
            ".PBBDGGDDDDGGDBBP.",
            ".PBBDDDDDDDDDDBBP.",
            "..BBBBBBBBBBBBBB..",
            "...PPPPPPPPPPPP...",
            "....PPPPPPPPPP....",
            ".....PPPPPPPP.....",
            "..................",
        },
        AvatarExpression.Yawn => new[]
        {
            "......PPPPPP......",
            "....PPPPPPPPPP....",
            "...PPPPPPPPPPPP...",
            "..BBBBBBBBBBBBBB..",
            "..BDDDDBBDDDDBB...",
            "..BDWDDBBDWDDBB...",
            "..BDDDDBBDDDDBB...",
            "..BBBBBBBBBBBBBB..",
            "..BBBBBBBBBBBBBB..",
            ".PBBBBBBBBBBBBBBP.",
            ".PBBDDDDDDDDDDBBP.",
            ".PBBDDDDDDDDDDBBP.",
            ".PBBDDDDDDDDDDBBP.",
            "..BBBBBBBBBBBBBB..",
            "...PPPPPPPPPPPP...",
            "....PPPPPPPPPP....",
            ".....PPPPPPPP.....",
            "..................",
        },
        AvatarExpression.YawnWide => new[]
        {
            "......PPPPPP......",
            "....PPPPPPPPPP....",
            "...PPPPPPPPPPPP...",
            "..BBBBBBBBBBBBBB..",
            "..BBBBBBBBBBBBBB..",
            "..BDDDDBBDDDDBB...",
            "..BBBBBBBBBBBBBB..",
            "..BBBBBBBBBBBBBB..",
            "..BBBBBBBBBBBBBB..",
            ".PBBBBBBBBBBBBBBP.",
            ".PBBDDDDDDDDDDBBP.",
            ".PBBDDDDDDDDDDBBP.",
            ".PBBDDDDDDDDDDBBP.",
            "..BBBBBBBBBBBBBB..",
            "...PPPPPPPPPPPP...",
            "....PPPPPPPPPP....",
            ".....PPPPPPPP.....",
            "..................",
        },
        AvatarExpression.Listening => new[]
        {
            "......PPPPPP......",
            "....RRRRRRRRRR....",
            "...PPPPPPPPPPPP...",
            "..BBBBBBBBBBBBBB..",
            "..BDDDDBBDDDDBB...",
            "..BDWDDBBDWDDBB...",
            "..BDDDDBBDDDDBB...",
            "..BBBBBBBBBBBBBB..",
            "..BBBBBBBBBBBBBB..",
            ".PBBBBBBBBBBBBBBP.",
            ".PBBDDDDDDDDDDBBP.",
            ".PBBDGGDDDDGGDBBP.",
            ".PBBDDDDDDDDDDBBP.",
            "..BBBBBBBBBBBBBB..",
            "...PPPPPPPPPPPP...",
            "....PPPPPPPPPP....",
            ".....PPPPPPPP.....",
            "..................",
        },
        AvatarExpression.Thinking => new[]
        {
            "......PPPPPP......",
            "....PPYYbbPPPP....",
            "...PPPPPPPPPPPP...",
            "..BBBBBBBBBBBBBB..",
            "..BDDDDBBDDDDBB...",
            "..BDWDDBBDWDDBB...",
            "..BDDDDBBDDDDBB...",
            "..BBBBBBBBBBBBBB..",
            "..BBBBBBBBBBBBBB..",
            ".PBBBBBBBBBBBBBBP.",
            ".PBBDDDDDDDDDDBBP.",
            ".PBBDDDDDDDDDDBBP.",
            ".PBBDDDDDDDDDDBBP.",
            "..BBBBBBBBBBBBBB..",
            "...PPPPPPPPPPPP...",
            "....PPPPPPPPPP....",
            ".....PPPPPPPP.....",
            "..................",
        },
        AvatarExpression.Speaking => new[]
        {
            "......PPPPPP......",
            "....PPPPPPPPPP....",
            "...PPPPPPPPPPPP...",
            "..BBBBBBBBBBBBBB..",
            "..BDDDDBBDDDDBB...",
            "..BDWDDBBDWDDBB...",
            "..BDDDDBBDDDDBB...",
            "..BBBBBBBBBBBBBB..",
            "..BBBBBBBBBBBBBB..",
            ".PBBBBBBBBBBBBBBP.",
            ".PBBDGGGGGGGGDBBP.",
            ".PBBDDDDDDDDDDBBP.",
            ".PBBDGGGGGGGGDBBP.",
            "..BBBBBBBBBBBBBB..",
            "...PPPPPPPPPPPP...",
            "....PPPPPPPPPP....",
            ".....PPPPPPPP.....",
            "..................",
        },
        AvatarExpression.Focused => new[]
        {
            "......PPPPPP......",
            "....OOOOOOOOOO....",
            "...PPPPPPPPPPPP...",
            "..BBBBBBBBBBBBBB..",
            "..BDDDDBBDDDDBB...",
            "..BDWDDBBDWDDBB...",
            "..BDDDDBBDDDDBB...",
            "..BBBBBBBBBBBBBB..",
            "..BBBBBBBBBBBBBB..",
            ".PBBBBBBBBBBBBBBP.",
            ".PBBDDDDDDDDDDBBP.",
            ".PBBDDDDDDDDDDBBP.",
            ".PBBDDDDDDDDDDBBP.",
            "..BBBBBBBBBBBBBB..",
            "...PPPPPPPPPPPP...",
            "....PPPPPPPPPP....",
            ".....PPPPPPPP.....",
            "..................",
        },
        AvatarExpression.Relaxed => new[]
        {
            "......PPPPPP......",
            "....PPPPPPPPPP....",
            "...PPPPPPPPPPPP...",
            "..BBBBBBBBBBBBBB..",
            "..BDDDDBBDDDDBB...",
            "..BDWDDBBDWDDBB...",
            "..BDDDDBBDDDDBB...",
            "..BBBBBBBBBBBBBB..",
            "..BBBBBBBBBBBBBB..",
            ".PBBBBBBBBBBBBBBP.",
            ".PBBDDDDDDDDDDBBP.",
            ".PBBDGGDDDDGGDBBP.",
            ".PBBDDDDDDDDDDBBP.",
            "..BBBBBBBBBBBBBB..",
            "...PPPPPPPPPPPP...",
            "....PPPPPPPPPP....",
            ".....PPPPPPPP.....",
            "..................",
        },
        AvatarExpression.Sleeping => new[]
        {
            "......PPPPPP......",
            "....PPPPPPPPPP....",
            "...PPPPPPPPPPPP...",
            "..BBBBBBBBBBBBBB..",
            "..BBBBBBBBBBBBBB..",
            "..BDDDDBBDDDDBB...",
            "..BBBBBBBBBBBBBB..",
            "..BBBBBBBBBBBBBB..",
            "..BBBBBBBBBBBBBB..",
            ".PBBBBBBBBBBBBBBP.",
            ".PBBDDDDDDDDDDBBP.",
            ".PBBDDDDDDDDDDBBP.",
            ".PBBDDDDDDDDDDBBP.",
            "..BBBBBBBBBBBBBB..",
            "...PPPPPPPPPPPP...",
            "....PPPPPPPPPP....",
            ".....PPPPPPPP.....",
            "..................",
        },
        _ => GetFrame(AvatarExpression.Normal),
    };
}
