namespace CopilotVoice.UI.Avatar;

/// <summary>
/// ASCII art data for the Copilot CLI robot avatar.
/// Based on the official GitHub Copilot CLI robot design.
/// </summary>
public static class PixelAvatarData
{
    // Legacy color support (unused with ASCII rendering)
    public const string ColorTransparent = "#00000000";
    public static string GetColor(char code) => ColorTransparent;

    // Shared rows that don't change between expressions
    private const string Row0 = "       ▄██████▄       ";
    private const string Row1 = "   ▄█▀▀▀▀▀██▀▀▀▀▀█▄   ";
    private const string Row2 = "  ▐█      ▐▌      █▌  ";
    private const string Row3 = "  ▐█▄    ▄██▄    ▄█▌  ";
    private const string Row4 = " ▄▄███████▀▀███████▄▄ ";
    private const string Row8 = "   ▀▀████████████▀▀   ";

    // Eye rows (rows 5-6) vary per expression
    // Normal: eyes open  ▄/█
    private const string EyeTop    = "████     ▄  ▄     ████";
    private const string EyeBot    = "████     █  █     ████";
    // Blink: eyes closed ─
    private const string EyeTopClosed = "████     ─  ─     ████";
    private const string EyeBotClosed = "████              ████"; // blank below
    // HalfBlink: eyes half ▀
    private const string EyeTopHalf   = "████     ▀  ▀     ████";
    private const string EyeBotHalf   = "████              ████";
    // Listening: eyes wide ◉
    private const string EyeTopListen = "████     ◉  ◉     ████";
    private const string EyeBotListen = "████              ████";
    // Thinking: eyes look right ▄ shifted
    private const string EyeTopThink  = "████      ▄  ▄    ████";
    private const string EyeBotThink  = "████      █  █    ████";
    // Sleeping: eyes as ─ ─ (same as blink)
    private const string EyeTopSleep  = "████     ─  ─     ████";
    private const string EyeBotSleep  = "████     ᶻ  ᶻ     ████";
    // Focused: eyes as ▪ (small squares)
    private const string EyeTopFocus  = "████     ■  ■     ████";
    private const string EyeBotFocus  = "████              ████";

    // Smile: happy eyes ^  ^
    private const string EyeTopSmile  = "████     ▀  ▀     ████";
    private const string EyeBotSmile  = "████              ████";
    // Cry: teardrop eyes ▄ with · tears
    private const string EyeTopCry    = "████     ▄  ▄     ████";
    private const string EyeBotCry    = "████     █· ·█    ████";

    // Mouth row (row 7) varies per expression
    private const string MouthNormal   = "▀███▄            ▄███▀";
    private const string MouthOpen     = "▀███▄    ▄▄▄▄    ▄███▀";
    private const string MouthWide     = "▀███▄   ▄████▄   ▄███▀";
    private const string MouthSmile    = "▀███▄     ‿‿     ▄███▀";
    private const string MouthRelaxed  = "▀███▄     ──     ▄███▀";
    private const string MouthSad      = "▀███▄     ⌢⌢     ▄███▀";
    private const string MouthZipper   = "▀███▄   ╶╫╫╫╫╴   ▄███▀";

    public static string[] GetFrame(AvatarExpression expression) => expression switch
    {
        AvatarExpression.Normal => new[]
            { Row0, Row1, Row2, Row3, Row4, EyeTop, EyeBot, MouthNormal, Row8 },

        AvatarExpression.Blink => new[]
            { Row0, Row1, Row2, Row3, Row4, EyeTopClosed, EyeBotClosed, MouthNormal, Row8 },

        AvatarExpression.HalfBlink => new[]
            { Row0, Row1, Row2, Row3, Row4, EyeTopHalf, EyeBotHalf, MouthNormal, Row8 },

        AvatarExpression.Yawn => new[]
            { Row0, Row1, Row2, Row3, Row4, EyeTopHalf, EyeBotHalf, MouthOpen, Row8 },

        AvatarExpression.YawnWide => new[]
            { Row0, Row1, Row2, Row3, Row4, EyeTopClosed, EyeBotClosed, MouthWide, Row8 },

        AvatarExpression.Listening => new[]
            { Row0, Row1, Row2, Row3, Row4, EyeTopListen, EyeBotListen, MouthNormal, Row8 },

        AvatarExpression.Thinking => new[]
            { Row0, Row1, Row2, Row3, Row4, EyeTopThink, EyeBotThink, MouthNormal, Row8 },

        AvatarExpression.Speaking => new[]
            { Row0, Row1, Row2, Row3, Row4, EyeTop, EyeBot, MouthOpen, Row8 },

        AvatarExpression.Focused => new[]
            { Row0, Row1, Row2, Row3, Row4, EyeTopFocus, EyeBotFocus, MouthRelaxed, Row8 },

        AvatarExpression.Relaxed => new[]
            { Row0, Row1, Row2, Row3, Row4, EyeTopHalf, EyeBotHalf, MouthSmile, Row8 },

        AvatarExpression.Sleeping => new[]
            { Row0, Row1, Row2, Row3, Row4, EyeTopSleep, EyeBotSleep, MouthRelaxed, Row8 },

        AvatarExpression.Smile => new[]
            { Row0, Row1, Row2, Row3, Row4, EyeTopSmile, EyeBotSmile, MouthSmile, Row8 },

        AvatarExpression.Cry => new[]
            { Row0, Row1, Row2, Row3, Row4, EyeTopCry, EyeBotCry, MouthSad, Row8 },

        AvatarExpression.Muted => new[]
            { Row0, Row1, Row2, Row3, Row4, EyeTop, EyeBot, MouthZipper, Row8 },

        _ => GetFrame(AvatarExpression.Normal),
    };
}
