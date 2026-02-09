using CopilotVoice.Sessions;
using Xunit;

namespace CopilotVoice.Tests.Sessions;

public class SessionDetectorTests
{
    [Theory]
    [InlineData("vbomfim  12345  0.0  0.1 123456 7890 s001 S  10:00AM  0:01.00 gh copilot suggest", true)]
    [InlineData("vbomfim  12345  0.0  0.1 123456 7890 s001 S  10:00AM  0:01.00 /usr/local/bin/gh copilot", true)]
    [InlineData("vbomfim  12345  0.0  0.1 123456 7890 s001 S  10:00AM  0:01.00 github-copilot --stdio", true)]
    [InlineData("vbomfim  12345  0.0  0.1 123456 7890 s001 S  10:00AM  0:01.00 copilot-cli run", true)]
    [InlineData("vbomfim  12345  0.0  0.1 123456 7890 s001 S  10:00AM  0:01.00 node @githubnext/github-copilot-cli", true)]
    [InlineData("vbomfim  12345  0.0  0.1 123456 7890 s001 S  10:00AM  0:01.00 /Applications/Visual Studio Code.app/Contents/Frameworks/Code Helper.app copilot", false)]
    [InlineData("vbomfim  12345  0.0  0.1 123456 7890 s001 S  10:00AM  0:01.00 Code - Insiders Helper (Plugin) copilot-language-server", false)]
    [InlineData("vbomfim  12345  0.0  0.1 123456 7890 s001 S  10:00AM  0:01.00 copilot-voice --register", false)]
    [InlineData("vbomfim  12345  0.0  0.1 123456 7890 s001 S  10:00AM  0:01.00 grep copilot", false)]
    [InlineData("vbomfim  12345  0.0  0.1 123456 7890 s001 S  10:00AM  0:01.00 node copilot-language-server --stdio", false)]
    [InlineData("vbomfim  12345  0.0  0.1 123456 7890 s001 S  10:00AM  0:01.00 vim ~/.config/copilot/config.json", false)]
    public void IsCopilotCliProcess_FiltersCorrectly(string processLine, bool expected)
    {
        Assert.Equal(expected, SessionDetector.IsCopilotCliProcess(processLine));
    }
}
