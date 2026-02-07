namespace CopilotVoice.UI.Avatar;

/// <summary>
/// Contract for avatar theme renderers.
/// </summary>
public interface IAvatarTheme
{
    string Name { get; }
    AvatarTheme ThemeType { get; }

    /// <summary>
    /// Renders the given expression as lines of ASCII/text art.
    /// </summary>
    string[] RenderFrame(AvatarExpression expression);
}
