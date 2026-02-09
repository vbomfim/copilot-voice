using CopilotVoice.Messaging;
using CopilotVoice.Sessions;
using Xunit;

namespace CopilotVoice.Tests.Sessions;

public class SessionManagerTests
{
    private static SessionManager CreateManager()
    {
        var detector = new SessionDetector();
        return new SessionManager(detector);
    }

    [Fact]
    public void RegisterSession_AddsSessionAndSetsRegistered()
    {
        var manager = CreateManager();
        var request = new RegisterRequest
        {
            Pid = 9999,
            WorkingDirectory = "/Users/dev/my-project",
            Label = "my project"
        };

        var session = manager.RegisterSession(request);

        Assert.Equal("registered-9999", session.Id);
        Assert.Equal(9999, session.ProcessId);
        Assert.Equal("/Users/dev/my-project", session.WorkingDirectory);
        Assert.True(session.IsRegistered);
        Assert.Equal("my project", session.TerminalTitle);
    }

    [Fact]
    public void RegisterSession_DuplicatePid_ReplacesExisting()
    {
        var manager = CreateManager();
        var first = new RegisterRequest { Pid = 100, WorkingDirectory = "/old", Label = "old" };
        var second = new RegisterRequest { Pid = 100, WorkingDirectory = "/new", Label = "new" };

        manager.RegisterSession(first);
        var session = manager.RegisterSession(second);

        var all = manager.GetAllSessions();
        Assert.Single(all, s => s.ProcessId == 100);
        Assert.Equal("/new", session.WorkingDirectory);
    }

    [Fact]
    public void GetAllSessions_RegisteredSessionsFirst()
    {
        var manager = CreateManager();
        manager.RegisterSession(new RegisterRequest
        {
            Pid = 5555,
            WorkingDirectory = "/Users/dev/registered-project"
        });

        var sessions = manager.GetAllSessions();
        Assert.NotEmpty(sessions);
        Assert.Equal(5555, sessions[0].ProcessId);
        Assert.True(sessions[0].IsRegistered);
    }
}
