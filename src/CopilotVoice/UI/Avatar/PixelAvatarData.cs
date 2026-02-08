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

    // 16 wide x 14 tall
    public static string[] GetFrame(AvatarExpression expression) => expression switch
    {
        AvatarExpression.Normal => new[]
        {
            //0123456789012345
            "......PPPP......",
            ".....PbbbbP.....",
            "...BBBBBBBBbB...",
            "..BBBBBBBBBBbB..",
            "..BDDDBBBDDDB...",
            "..BDDDBBBDDDB...",
            "..BBBBBbBBBBBB..",
            "..BBBBBbBBBBBB..",
            ".PBBDDDDDDDDBP.",
            ".PPBDDDDDDDDBPP",
            ".PPBDDDGDGDDPPP",
            ".PPBDDDGDGDDPPP",
            "..PPDDDDDDDDPP.",
            "...PPPPPPPPPP...",
        },
        AvatarExpression.Blink => new[]
        {
            "......PPPP......",
            ".....PbbbbP.....",
            "...BBBBBBBBbB...",
            "..BBBBBBBBBBbB..",
            "..BBBBBBBBBBB...",
            "..BDDDBBBDDDB...",
            "..BBBBBbBBBBBB..",
            "..BBBBBbBBBBBB..",
            ".PBBDDDDDDDDBP.",
            ".PPBDDDDDDDDBPP",
            ".PPBDDDGDGDDPPP",
            ".PPBDDDGDGDDPPP",
            "..PPDDDDDDDDPP.",
            "...PPPPPPPPPP...",
        },
        AvatarExpression.HalfBlink => new[]
        {
            "......PPPP......",
            ".....PbbbbP.....",
            "...BBBBBBBBbB...",
            "..BBBBBBBBBBbB..",
            "..BDBDBBBDBDB...",
            "..BDDDBBBDDDB...",
            "..BBBBBbBBBBBB..",
            "..BBBBBbBBBBBB..",
            ".PBBDDDDDDDDBP.",
            ".PPBDDDDDDDDBPP",
            ".PPBDDDGDGDDPPP",
            ".PPBDDDGDGDDPPP",
            "..PPDDDDDDDDPP.",
            "...PPPPPPPPPP...",
        },
        AvatarExpression.Yawn => new[]
        {
            "......PPPP......",
            ".....PbbbbP.....",
            "...BBBBBBBBbB...",
            "..BBBBBBBBBBbB..",
            "..BDDDBBBDDDB...",
            "..BDDDBBBDDDB...",
            "..BBBBBbBBBBBB..",
            "..BBBBBbBBBBBB..",
            ".PBBDDDDDDDDBP.",
            ".PPBDDDDDDDDPPP",
            ".PPBDDDDDDDDBPP",
            ".PPBDDDDDDDDPPP",
            "..PPDDDDDDDDPP.",
            "...PPPPPPPPPP...",
        },
        AvatarExpression.YawnWide => new[]
        {
            "......PPPP......",
            ".....PbbbbP.....",
            "...BBBBBBBBbB...",
            "..BBBBBBBBBBbB..",
            "..BBBBBBBBBBB...",
            "..BDDDBBBDDDB...",
            "..BBBBBbBBBBBB..",
            "..BBBBBbBBBBBB..",
            ".PBBDDDDDDDDBP.",
            ".PPBDDDDDDDDPPP",
            ".PPBDDDDDDDDBPP",
            ".PPBDDDDDDDDPPP",
            "..PPDDDDDDDDPP.",
            "...PPPPPPPPPP...",
        },
        AvatarExpression.Listening => new[]
        {
            "......PPPP......",
            ".....PRRRRP.....",
            "...BBBBBBBBbB...",
            "..BBBBBBBBBBbB..",
            "..BDDDBBBDDDB...",
            "..BDWDBBBDWDB...",
            "..BBBBBbBBBBBB..",
            "..BBBBBbBBBBBB..",
            ".PBBDDDDDDDDBP.",
            ".PPBDDDDDDDDBPP",
            ".PPBDDDGDGDDPPP",
            ".PPBDDDGDGDDPPP",
            "..PPDDDDDDDDPP.",
            "...PPPPPPPPPP...",
        },
        AvatarExpression.Thinking => new[]
        {
            "......PPPP......",
            ".....PYYbbP.....",
            "...BBBBBBBBbB...",
            "..BBBBBBBBBBbB..",
            "..BDBDBBBDDDB...",
            "..BDDDBBBDDDB...",
            "..BBBBBbBBBBBB..",
            "..BBBBBbBBBBBB..",
            ".PBBDDDDDDDDBP.",
            ".PPBDDDDDDDDBPP",
            ".PPBDDDDDDDDBPP",
            ".PPBDDDDDDDDPPP",
            "..PPDDDDDDDDPP.",
            "...PPPPPPPPPP...",
        },
        AvatarExpression.Speaking => new[]
        {
            "......PPPP......",
            ".....PbbbbP.....",
            "...BBBBBBBBbB...",
            "..BBBBBBBBBBbB..",
            "..BDDDBBBDDDB...",
            "..BDDDBBBDDDB...",
            "..BBBBBbBBBBBB..",
            "..BBBBBbBBBBBB..",
            ".PBBDDDDDDDDBP.",
            ".PPBDDGGGGGDPPP",
            ".PPBDDDDDDDDBPP",
            ".PPBDDGGGGGDPPP",
            "..PPDDDDDDDDPP.",
            "...PPPPPPPPPP...",
        },
        AvatarExpression.Focused => new[]
        {
            "......PPPP......",
            ".....POOOOP.....",
            "...BBBBBBBBbB...",
            "..BBBBBBBBBBbB..",
            "..BDDDBBBDDDB...",
            "..BDDDBBBDDDB...",
            "..BBBBBbBBBBBB..",
            "..BBBBBbBBBBBB..",
            ".PBBDDDDDDDDBP.",
            ".PPBDDDDDDDDBPP",
            ".PPBDDDDDDDDPPP",
            ".PPBDDDDDDDDPPP",
            "..PPDDDDDDDDPP.",
            "...PPPPPPPPPP...",
        },
        AvatarExpression.Relaxed => new[]
        {
            "......PPPP......",
            ".....PbbbbP.....",
            "...BBBBBBBBbB...",
            "..BBBBBBBBBBbB..",
            "..BBBBBBBBBBB...",
            "..BDDDBBBDDDB...",
            "..BBBBBbBBBBBB..",
            "..BBBBBbBBBBBB..",
            ".PBBDDDDDDDDBP.",
            ".PPBDDDDDDDDBPP",
            ".PPBDDDGDGDDPPP",
            ".PPBDDDGDGDDPPP",
            "..PPDDDDDDDDPP.",
            "...PPPPPPPPPP...",
        },
        AvatarExpression.Sleeping => new[]
        {
            "......PPPP......",
            ".....PbbbbP.....",
            "...BBBBBBBBbB...",
            "..BBBBBBBBBBbB..",
            "..BBBBBBBBBBB...",
            "..BDDDBBBDDDB...",
            "..BBBBBbBBBBBB..",
            "..BBBBBbBBBBBB..",
            ".PBBDDDDDDDDBP.",
            ".PPBDDDDDDDDBPP",
            ".PPBDDDDDDDDBPP",
            ".PPBDDDDDDDDPPP",
            "..PPDDDDDDDDPP.",
            "...PPPPPPPPPP...",
        },
        _ => GetFrame(AvatarExpression.Normal),
    };
}
