using CopilotVoice.Sessions;
using Xunit;

namespace CopilotVoice.Tests.Sessions;

public class CopilotSessionTests
{
    [Fact]
    public void Label_WithWorkingDirectory_ReturnsBasename()
    {
        var session = new CopilotSession
        {
            ProcessId = 123,
            WorkingDirectory = "/Users/dev/projects/my-app",
            TerminalApp = "Terminal"
        };

        Assert.Equal("my-app", session.Label);
    }

    [Fact]
    public void Label_WithWorkingDirectoryAndNonDefaultTerminal_IncludesTerminalApp()
    {
        var session = new CopilotSession
        {
            ProcessId = 123,
            WorkingDirectory = "/Users/dev/projects/my-app",
            TerminalApp = "Ghostty"
        };

        Assert.Equal("my-app (Ghostty)", session.Label);
    }

    [Fact]
    public void Label_WithoutWorkingDirectory_FallsBackToTerminalTitle()
    {
        var session = new CopilotSession
        {
            ProcessId = 123,
            WorkingDirectory = "unknown",
            TerminalTitle = "Copilot CLI (PID 123)",
            TerminalApp = "Terminal"
        };

        Assert.Equal("Copilot CLI (PID 123)", session.Label);
    }

    [Fact]
    public void Label_WithNothing_FallsBackToSessionPid()
    {
        var session = new CopilotSession
        {
            ProcessId = 456,
            WorkingDirectory = "unknown",
            TerminalApp = "Terminal"
        };

        Assert.Equal("Session 456", session.Label);
    }

    [Fact]
    public void Label_DefaultTerminal_DoesNotAppendTerminalApp()
    {
        var session = new CopilotSession
        {
            ProcessId = 123,
            WorkingDirectory = "/home/user/project",
            TerminalApp = "Terminal"
        };

        Assert.Equal("project", session.Label);
    }
}
