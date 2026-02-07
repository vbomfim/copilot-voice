using CopilotVoice.Hotkey;
using SharpHook.Data;

namespace CopilotVoice.Tests.Hotkey;

public class HotkeyListenerTests
{
    [Theory]
    [InlineData("a", KeyCode.VcA)]
    [InlineData("Z", KeyCode.VcZ)]
    [InlineData("m", KeyCode.VcM)]
    public void ParseKeyCode_ValidLetter_ReturnsCorrect(string input, KeyCode expected)
    {
        var result = HotkeyListener.ParseKeyCode(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("ctrl", KeyCode.VcLeftControl)]
    [InlineData("control", KeyCode.VcLeftControl)]
    [InlineData("shift", KeyCode.VcLeftShift)]
    [InlineData("alt", KeyCode.VcLeftAlt)]
    [InlineData("option", KeyCode.VcLeftAlt)]
    [InlineData("meta", KeyCode.VcLeftMeta)]
    [InlineData("cmd", KeyCode.VcLeftMeta)]
    [InlineData("command", KeyCode.VcLeftMeta)]
    public void ParseKeyCode_Modifier_ReturnsCorrect(string input, KeyCode expected)
    {
        var result = HotkeyListener.ParseKeyCode(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("foo")]
    [InlineData("f99")]
    [InlineData("!!")]
    public void ParseKeyCode_InvalidKey_Throws(string input)
    {
        Assert.Throws<ArgumentException>(() => HotkeyListener.ParseKeyCode(input));
    }
}
