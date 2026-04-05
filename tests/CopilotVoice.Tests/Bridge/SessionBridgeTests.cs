using CopilotVoice.Bridge;
using Xunit;

namespace CopilotVoice.Tests.Bridge;

public class SessionBridgeTests
{
    private static SessionBridge CreateBridge() => new();

    // --- Message Routing ---

    [Fact]
    public void OnMessageReceived_FiresMessageReceivedEvent()
    {
        var bridge = CreateBridge();
        CliMessage? received = null;
        bridge.MessageReceived += msg => received = msg;

        var message = new CliMessage("assistant.message", "Hello world", "msg-1", 1712345678000);
        bridge.OnMessageReceived(message);

        Assert.NotNull(received);
        Assert.Equal("Hello world", received!.Content);
        Assert.Equal("msg-1", received.MessageId);
    }

    [Fact]
    public void OnEventReceived_FiresEventReceivedEvent()
    {
        var bridge = CreateBridge();
        CliEvent? received = null;
        bridge.EventReceived += evt => received = evt;

        var evt = new CliEvent("session.start", null, 1712345678000);
        bridge.OnEventReceived(evt);

        Assert.NotNull(received);
        Assert.Equal("session.start", received!.Type);
    }

    // --- Session Tracking ---

    [Fact]
    public void ConnectedSessions_InitiallyEmpty()
    {
        var bridge = CreateBridge();
        Assert.Empty(bridge.ConnectedSessions);
    }

    [Fact]
    public async Task GetCommandStream_CreatesSession()
    {
        var bridge = CreateBridge();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Start reading (creates session), then cancel
        try
        {
            await foreach (var _ in bridge.GetCommandStream("session-1", cts.Token))
            {
                break;
            }
        }
        catch (OperationCanceledException) { }

        Assert.Contains("session-1", bridge.ConnectedSessions);
    }

    [Fact]
    public void RemoveSession_RemovesFromConnected()
    {
        var bridge = CreateBridge();

        // Force session creation via QueueCommand
        bridge.QueueCommand("session-1", new SendPromptCommand("test"));
        Assert.Contains("session-1", bridge.ConnectedSessions);

        bridge.RemoveSession("session-1");
        Assert.DoesNotContain("session-1", bridge.ConnectedSessions);
    }

    [Fact]
    public void RemoveSession_NonexistentSession_NoError()
    {
        var bridge = CreateBridge();
        bridge.RemoveSession("nonexistent"); // should not throw
    }

    // --- Command Queuing ---

    [Fact]
    public async Task QueueCommand_DeliveredViaCommandStream()
    {
        var bridge = CreateBridge();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var command = new SendPromptCommand("Refactor auth module");

        // Queue command first (creates session implicitly)
        bridge.QueueCommand("session-1", command);

        // Read from stream
        SendPromptCommand? received = null;
        await foreach (var cmd in bridge.GetCommandStream("session-1", cts.Token))
        {
            received = cmd;
            break; // got one, stop
        }

        Assert.NotNull(received);
        Assert.Equal("Refactor auth module", received!.Prompt);
    }

    [Fact]
    public async Task QueueCommand_OnlyDeliveredToTargetSession()
    {
        var bridge = CreateBridge();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Queue command only for session-1
        bridge.QueueCommand("session-1", new SendPromptCommand("For session 1"));

        // Try reading from session-2 (should timeout with nothing)
        var received = new List<SendPromptCommand>();
        try
        {
            await foreach (var cmd in bridge.GetCommandStream("session-2", cts.Token))
            {
                received.Add(cmd);
            }
        }
        catch (OperationCanceledException) { }

        Assert.Empty(received);
    }

    [Fact]
    public async Task QueueCommand_MultipleCommandsDeliveredInOrder()
    {
        var bridge = CreateBridge();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        bridge.QueueCommand("session-1", new SendPromptCommand("First"));
        bridge.QueueCommand("session-1", new SendPromptCommand("Second"));
        bridge.QueueCommand("session-1", new SendPromptCommand("Third"));

        var received = new List<string>();
        await foreach (var cmd in bridge.GetCommandStream("session-1", cts.Token))
        {
            received.Add(cmd.Prompt);
            if (received.Count == 3) break;
        }

        Assert.Equal(new[] { "First", "Second", "Third" }, received);
    }

    // --- Multiple Sessions ---

    [Fact]
    public void MultipleSessions_TrackedIndependently()
    {
        var bridge = CreateBridge();

        bridge.QueueCommand("session-a", new SendPromptCommand("For A"));
        bridge.QueueCommand("session-b", new SendPromptCommand("For B"));

        var sessions = bridge.ConnectedSessions;
        Assert.Equal(2, sessions.Count);
        Assert.Contains("session-a", sessions);
        Assert.Contains("session-b", sessions);
    }

    [Fact]
    public async Task MultipleSessions_CommandsRoutedCorrectly()
    {
        var bridge = CreateBridge();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        bridge.QueueCommand("session-a", new SendPromptCommand("For A"));
        bridge.QueueCommand("session-b", new SendPromptCommand("For B"));

        // Read from session-a
        SendPromptCommand? fromA = null;
        await foreach (var cmd in bridge.GetCommandStream("session-a", cts.Token))
        {
            fromA = cmd;
            break;
        }

        // Read from session-b
        SendPromptCommand? fromB = null;
        await foreach (var cmd in bridge.GetCommandStream("session-b", cts.Token))
        {
            fromB = cmd;
            break;
        }

        Assert.Equal("For A", fromA!.Prompt);
        Assert.Equal("For B", fromB!.Prompt);
    }

    // --- Edge Cases ---

    [Fact]
    public void OnMessageReceived_NoSubscribers_NoError()
    {
        var bridge = CreateBridge();
        // Should not throw even with no event subscribers
        bridge.OnMessageReceived(new CliMessage("test", "content", "id-1", 123));
    }

    [Fact]
    public void OnEventReceived_NoSubscribers_NoError()
    {
        var bridge = CreateBridge();
        // Should not throw even with no event subscribers
        bridge.OnEventReceived(new CliEvent("test", null, 123));
    }
}
