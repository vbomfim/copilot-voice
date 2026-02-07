namespace CopilotVoice.UI.Avatar.Themes;

/// <summary>
/// Audio waveform bars that animate with speech and flatten when idle.
/// </summary>
public sealed class WaveformTheme : IAvatarTheme
{
    public string Name => "Waveform";
    public AvatarTheme ThemeType => AvatarTheme.Waveform;

    public string[] RenderFrame(AvatarExpression expression) => expression switch
    {
        AvatarExpression.Normal => new[]
        {
            "  ▁ ▂ ▁ ▂ ▁  ",
            "  █ █ █ █ █  ",
            "  ▔ ▔ ▔ ▔ ▔  ",
        },
        AvatarExpression.Blink => new[]
        {
            "  ▁ ▁ ▁ ▁ ▁  ",
            "  ▄ ▄ ▄ ▄ ▄  ",
            "  ▔ ▔ ▔ ▔ ▔  ",
        },
        AvatarExpression.HalfBlink => new[]
        {
            "  ▁ ▂ ▁ ▁ ▁  ",
            "  █ ▄ █ ▄ █  ",
            "  ▔ ▔ ▔ ▔ ▔  ",
        },
        AvatarExpression.Yawn => new[]
        {
            "  ▁ ▁ ▃ ▁ ▁  ",
            "  ▄ ▄ █ ▄ ▄  ",
            "  ▔ ▔ ▔ ▔ ▔  ",
        },
        AvatarExpression.YawnWide => new[]
        {
            "  ▁ ▁ ▅ ▁ ▁  ",
            "  ▂ ▂ █ ▂ ▂  ",
            "  ▔ ▔ ▔ ▔ ▔  ",
        },
        AvatarExpression.Listening => new[]
        {
            "  ▃ ▅ ▃ ▅ ▃  ",
            "  █ █ █ █ █  ",
            "  ▔ ▔ ▔ ▔ ▔  ",
        },
        AvatarExpression.Thinking => new[]
        {
            "  ▂ ▁ ▂ ▁ ▂  ",
            "  █ ▄ █ ▄ █  ",
            "  ▔ ▔ ▔ ▔ ▔  ",
        },
        AvatarExpression.Speaking => new[]
        {
            "  ▃ ▇ ▅ ▇ ▃  ",
            "  █ █ █ █ █  ",
            "  ▔ ▔ ▔ ▔ ▔  ",
        },
        AvatarExpression.Focused => new[]
        {
            "  ▂ ▂ ▆ ▂ ▂  ",
            "  █ █ █ █ █  ",
            "  ▔ ▔ ▔ ▔ ▔  ",
        },
        AvatarExpression.Relaxed => new[]
        {
            "  ▁ ▁ ▂ ▁ ▁  ",
            "  ▄ ▄ █ ▄ ▄  ",
            "  ▔ ▔ ▔ ▔ ▔  ",
        },
        AvatarExpression.Sleeping => new[]
        {
            "  ▁ ▁ ▁ ▁ ▁  ",
            "  ▂ ▂ ▂ ▂ ▂  ",
            "  ▔ ▔ ▔ ▔ ▔  ",
            "        zzz  ",
        },
        _ => RenderFrame(AvatarExpression.Normal),
    };
}
