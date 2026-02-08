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

    // 18 wide x 18 tall — matches GitHub Copilot robot face
    public static string[] GetFrame(AvatarExpression expression) => expression switch
    {
        AvatarExpression.Normal => new[]
        {
            //012345678901234567
            "......PPPP........",
            ".....PPPPPP.......",
            "...bBBBBBBBBb.....",
            "..bBBBBBBBBBBb....",
            "..BBDDDBBBDDDB....",
            "..BBDDDBBBDDDB....",
            "..BBBBBBBBBBBBb...",
            "...BBBBDBBBBBb....",
            ".PBBBDDDDDDDBbP...",
            "PPBBBDDDDDDDBbPP..",
            "PPBBDDDDDDDDDBPP..",
            "PPBBDDDDDDDDDPPP..",
            "PPBBDDDDGDGDDPPP..",
            ".PPBDDDDGDGDDPPP..",
            "..PPPDDDDDDDPPP...",
            "...PPPPPPPPPPP....",
            "....PPPPPPPPP.....",
            "..................",
        },
        AvatarExpression.Blink => new[]
        {
            "......PPPP........",
            ".....PPPPPP.......",
            "...bBBBBBBBBb.....",
            "..bBBBBBBBBBBb....",
            "..BBBBBBBBBBBBB...",
            "..BBDDDBBBDDDB....",
            "..BBBBBBBBBBBBb...",
            "...BBBBDBBBBBb....",
            ".PBBBDDDDDDDBbP...",
            "PPBBBDDDDDDDBbPP..",
            "PPBBDDDDDDDDDBPP..",
            "PPBBDDDDDDDDDPPP..",
            "PPBBDDDDGDGDDPPP..",
            ".PPBDDDDGDGDDPPP..",
            "..PPPDDDDDDDPPP...",
            "...PPPPPPPPPPP....",
            "....PPPPPPPPP.....",
            "..................",
        },
        AvatarExpression.HalfBlink => new[]
        {
            "......PPPP........",
            ".....PPPPPP.......",
            "...bBBBBBBBBb.....",
            "..bBBBBBBBBBBb....",
            "..BBDBDBBBDBDB....",
            "..BBDDDBBBDDDB....",
            "..BBBBBBBBBBBBb...",
            "...BBBBDBBBBBb....",
            ".PBBBDDDDDDDBbP...",
            "PPBBBDDDDDDDBbPP..",
            "PPBBDDDDDDDDDBPP..",
            "PPBBDDDDDDDDDPPP..",
            "PPBBDDDDGDGDDPPP..",
            ".PPBDDDDGDGDDPPP..",
            "..PPPDDDDDDDPPP...",
            "...PPPPPPPPPPP....",
            "....PPPPPPPPP.....",
            "..................",
        },
        AvatarExpression.Yawn => new[]
        {
            "......PPPP........",
            ".....PPPPPP.......",
            "...bBBBBBBBBb.....",
            "..bBBBBBBBBBBb....",
            "..BBDDDBBBDDDB....",
            "..BBDDDBBBDDDB....",
            "..BBBBBBBBBBBBb...",
            "...BBBBDBBBBBb....",
            ".PBBBDDDDDDDBbP...",
            "PPBBBDDDDDDDBbPP..",
            "PPBBDDDDDDDDDBPP..",
            "PPBBDDDDDDDDDPPP..",
            "PPBBDDDDDDDDPPPP..",
            ".PPBDDDDDDDDPPP...",
            "..PPPDDDDDDDPPP...",
            "...PPPPPPPPPPP....",
            "....PPPPPPPPP.....",
            "..................",
        },
        AvatarExpression.YawnWide => new[]
        {
            "......PPPP........",
            ".....PPPPPP.......",
            "...bBBBBBBBBb.....",
            "..bBBBBBBBBBBb....",
            "..BBBBBBBBBBBBB...",
            "..BBDDDBBBDDDB....",
            "..BBBBBBBBBBBBb...",
            "...BBBBDBBBBBb....",
            ".PBBBDDDDDDDBbP...",
            "PPBBBDDDDDDDBbPP..",
            "PPBBDDDDDDDDDBPP..",
            "PPBBDDDDDDDDDPPP..",
            "PPBBDDDDDDDDPPPP..",
            ".PPBDDDDDDDDPPP...",
            "..PPPDDDDDDDPPP...",
            "...PPPPPPPPPPP....",
            "....PPPPPPPPP.....",
            "..................",
        },
        AvatarExpression.Listening => new[]
        {
            "......PPPP........",
            ".....RRRRRR.......",
            "...bBBBBBBBBb.....",
            "..bBBBBBBBBBBb....",
            "..BBDDDBBBDDDB....",
            "..BBDWDBBBDWDB....",
            "..BBBBBBBBBBBBb...",
            "...BBBBDBBBBBb....",
            ".PBBBDDDDDDDBbP...",
            "PPBBBDDDDDDDBbPP..",
            "PPBBDDDDDDDDDBPP..",
            "PPBBDDDDDDDDDPPP..",
            "PPBBDDDDGDGDDPPP..",
            ".PPBDDDDGDGDDPPP..",
            "..PPPDDDDDDDPPP...",
            "...PPPPPPPPPPP....",
            "....PPPPPPPPP.....",
            "..................",
        },
        AvatarExpression.Thinking => new[]
        {
            "......PPPP........",
            ".....PYYbbP.......",
            "...bBBBBBBBBb.....",
            "..bBBBBBBBBBBb....",
            "..BBDBDBBBDDDB....",
            "..BBDDDBBBDDDB....",
            "..BBBBBBBBBBBBb...",
            "...BBBBDBBBBBb....",
            ".PBBBDDDDDDDBbP...",
            "PPBBBDDDDDDDBbPP..",
            "PPBBDDDDDDDDDBPP..",
            "PPBBDDDDDDDDDPPP..",
            "PPBBDDDDDDDDPPPP..",
            ".PPBDDDDDDDDPPP...",
            "..PPPDDDDDDDPPP...",
            "...PPPPPPPPPPP....",
            "....PPPPPPPPP.....",
            "..................",
        },
        AvatarExpression.Speaking => new[]
        {
            "......PPPP........",
            ".....PPPPPP.......",
            "...bBBBBBBBBb.....",
            "..bBBBBBBBBBBb....",
            "..BBDDDBBBDDDB....",
            "..BBDDDBBBDDDB....",
            "..BBBBBBBBBBBBb...",
            "...BBBBDBBBBBb....",
            ".PBBBDDDDDDDBbP...",
            "PPBBBDGGGGGDBbPP..",
            "PPBBDDDDDDDDDBPP..",
            "PPBBDDGGGGGDDPPP..",
            "PPBBDDDDDDDDPPPP..",
            ".PPBDDDDDDDDPPP...",
            "..PPPDDDDDDDPPP...",
            "...PPPPPPPPPPP....",
            "....PPPPPPPPP.....",
            "..................",
        },
        AvatarExpression.Focused => new[]
        {
            "......PPPP........",
            ".....OOOOOO.......",
            "...bBBBBBBBBb.....",
            "..bBBBBBBBBBBb....",
            "..BBDDDBBBDDDB....",
            "..BBDDDBBBDDDB....",
            "..BBBBBBBBBBBBb...",
            "...BBBBDBBBBBb....",
            ".PBBBDDDDDDDBbP...",
            "PPBBBDDDDDDDBbPP..",
            "PPBBDDDDDDDDDBPP..",
            "PPBBDDDDDDDDDPPP..",
            "PPBBDDDDDDDDPPPP..",
            ".PPBDDDDDDDDPPP...",
            "..PPPDDDDDDDPPP...",
            "...PPPPPPPPPPP....",
            "....PPPPPPPPP.....",
            "..................",
        },
        AvatarExpression.Relaxed => new[]
        {
            "......PPPP........",
            ".....PPPPPP.......",
            "...bBBBBBBBBb.....",
            "..bBBBBBBBBBBb....",
            "..BBBBBBBBBBBBB...",
            "..BBDDDBBBDDDB....",
            "..BBBBBBBBBBBBb...",
            "...BBBBDBBBBBb....",
            ".PBBBDDDDDDDBbP...",
            "PPBBBDDDDDDDBbPP..",
            "PPBBDDDDDDDDDBPP..",
            "PPBBDDDDDDDDDPPP..",
            "PPBBDDDDGDGDDPPP..",
            ".PPBDDDDGDGDDPPP..",
            "..PPPDDDDDDDPPP...",
            "...PPPPPPPPPPP....",
            "....PPPPPPPPP.....",
            "..................",
        },
        AvatarExpression.Sleeping => new[]
        {
            "......PPPP........",
            ".....PPPPPP.......",
            "...bBBBBBBBBb.....",
            "..bBBBBBBBBBBb....",
            "..BBBBBBBBBBBBB...",
            "..BBDDDBBBDDDB....",
            "..BBBBBBBBBBBBb...",
            "...BBBBDBBBBBb....",
            ".PBBBDDDDDDDBbP...",
            "PPBBBDDDDDDDBbPP..",
            "PPBBDDDDDDDDDBPP..",
            "PPBBDDDDDDDDDPPP..",
            "PPBBDDDDDDDDPPPP..",
            ".PPBDDDDDDDDPPP...",
            "..PPPDDDDDDDPPP...",
            "...PPPPPPPPPPP....",
            "....PPPPPPPPP.....",
            "..................",
        },
        _ => GetFrame(AvatarExpression.Normal),
    };
}
