namespace CopilotVoice.UI.Avatar.Themes;

/// <summary>
/// Abstract diamond and circle patterns for each expression.
/// </summary>
public sealed class SymbolsTheme : IAvatarTheme
{
    public string Name => "Symbols";
    public AvatarTheme ThemeType => AvatarTheme.Symbols;

    public string[] RenderFrame(AvatarExpression expression) => expression switch
    {
        AvatarExpression.Normal => new[]
        {
            "    ◇    ",
            "  ◇ ● ◇  ",
            "    ◇    ",
        },
        AvatarExpression.Blink => new[]
        {
            "    ◇    ",
            "  ◇ ─ ◇  ",
            "    ◇    ",
        },
        AvatarExpression.HalfBlink => new[]
        {
            "    ◇    ",
            "  ◇ ◐ ◇  ",
            "    ◇    ",
        },
        AvatarExpression.Yawn => new[]
        {
            "    ◇    ",
            "  ◇ ○ ◇  ",
            "    ◇    ",
        },
        AvatarExpression.YawnWide => new[]
        {
            "    ◇    ",
            "  ◇ ◎ ◇  ",
            "    ◇    ",
        },
        AvatarExpression.Listening => new[]
        {
            "    ◆    ",
            "  ◆ ● ◆  ",
            "    ◆    ",
        },
        AvatarExpression.Thinking => new[]
        {
            "    ◇    ",
            "  ◈ ◑ ◈  ",
            "    ◇    ",
        },
        AvatarExpression.Speaking => new[]
        {
            "   ◆◇◆   ",
            "  ◆ ● ◆  ",
            "   ◆◇◆   ",
        },
        AvatarExpression.Focused => new[]
        {
            "    ◆    ",
            "  ◇ ◆ ◇  ",
            "    ◆    ",
        },
        AvatarExpression.Relaxed => new[]
        {
            "    ◇    ",
            "  ◇ ◡ ◇  ",
            "    ◇    ",
        },
        AvatarExpression.Sleeping => new[]
        {
            "    ◇   z",
            "  ◇ – ◇  ",
            "    ◇    ",
            "     zzz ",
        },
        AvatarExpression.Concerned => new[]
        {
            "    ◆    ",
            "  ◆ ⚠ ◆  ",
            "    ◆    ",
            "     !   ",
        },
        _ => RenderFrame(AvatarExpression.Normal),
    };
}
