using System.Text.Json;
using CopilotVoice.Voice;

namespace CopilotVoice.Tests.Voice;

public class VoiceLiveSessionTests : IAsyncDisposable
{
    private readonly FakeRealtimeConnection _connection = new();
    private readonly VoiceLiveConfig _config = new(
        Endpoint: "https://test.openai.azure.com",
        ApiKey: "test-key",
        Model: "gpt-4o-realtime-preview",
        Voice: "alloy",
        SystemInstructions: "You are a test assistant."
    );

    private VoiceLiveSession? _session;

    private VoiceLiveSession CreateSession()
    {
        _session = new VoiceLiveSession(_config, _connection, () => new FakeRealtimeConnection());
        return _session;
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            _connection.CompleteServerStream();
            await _session.DisposeAsync();
        }
    }

    // --- AC1: ConnectAsync establishes WebSocket with session config ---

    [Fact]
    public async Task StartAsync_ConnectsToCorrectUri()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);

        Assert.NotNull(_connection.ConnectedUri);
        Assert.Contains("openai/realtime", _connection.ConnectedUri!.ToString());
        Assert.Contains("gpt-4o-realtime-preview", _connection.ConnectedUri.ToString());
        Assert.Contains("api-version=2025-04-01-preview", _connection.ConnectedUri.ToString());
    }

    [Fact]
    public async Task StartAsync_SetsApiKeyHeader()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);

        Assert.NotNull(_connection.ConnectedHeaders);
        Assert.Equal("test-key", _connection.ConnectedHeaders!["api-key"]);
    }

    [Fact]
    public async Task StartAsync_SendsSessionUpdateEvent()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);

        var sentJson = await _connection.ReadClientEventAsync();
        var doc = JsonDocument.Parse(sentJson);
        var root = doc.RootElement;

        Assert.Equal("session.update", root.GetProperty("type").GetString());

        var sessionObj = root.GetProperty("session");
        Assert.Equal("alloy", sessionObj.GetProperty("voice").GetString());
        Assert.Contains("test assistant", sessionObj.GetProperty("instructions").GetString()!);
        Assert.Equal("pcm16", sessionObj.GetProperty("input_audio_format").GetString());
        Assert.Equal("pcm16", sessionObj.GetProperty("output_audio_format").GetString());
        Assert.Equal("auto", sessionObj.GetProperty("tool_choice").GetString());
    }

    [Fact]
    public async Task StartAsync_SessionUpdateIncludesTools()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);

        var sentJson = await _connection.ReadClientEventAsync();
        var doc = JsonDocument.Parse(sentJson);
        var tools = doc.RootElement.GetProperty("session").GetProperty("tools");

        var toolNames = tools.EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToList();

        Assert.Contains("send_to_cli", toolNames);
        Assert.Contains("get_session_context", toolNames);
        Assert.Contains("get_file_content", toolNames);
        Assert.Contains("set_avatar", toolNames);
    }

    [Fact]
    public async Task StartAsync_SessionUpdateIncludesModalities()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);

        var sentJson = await _connection.ReadClientEventAsync();
        var doc = JsonDocument.Parse(sentJson);
        var modalities = doc.RootElement.GetProperty("session").GetProperty("modalities");

        var modalList = modalities.EnumerateArray()
            .Select(m => m.GetString())
            .ToList();

        Assert.Contains("audio", modalList);
        Assert.Contains("text", modalList);
    }

    // --- AC2: SendAudioAsync streams audio ---

    [Fact]
    public async Task SendAudioAsync_SendsInputAudioBufferAppend()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);
        await _connection.ReadClientEventAsync(); // drain session.update

        var audioData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        await session.SendAudioAsync(audioData);

        var sentJson = await _connection.ReadClientEventAsync();
        var doc = JsonDocument.Parse(sentJson);

        Assert.Equal("input_audio_buffer.append", doc.RootElement.GetProperty("type").GetString());

        var base64 = doc.RootElement.GetProperty("audio").GetString();
        Assert.Equal(Convert.ToBase64String(audioData), base64);
    }

    [Fact]
    public async Task CommitAudioAsync_SendsCommitEvent()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);
        await _connection.ReadClientEventAsync(); // drain session.update

        await session.CommitAudioAsync();

        var sentJson = await _connection.ReadClientEventAsync();
        var doc = JsonDocument.Parse(sentJson);

        Assert.Equal("input_audio_buffer.commit", doc.RootElement.GetProperty("type").GetString());
    }

    // --- AC3: AudioReceived fires with response chunks ---

    [Fact]
    public async Task AudioReceived_FiresOnAudioDelta()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);

        ReadOnlyMemory<byte>? receivedAudio = null;
        var tcs = new TaskCompletionSource();
        session.AudioReceived += audio =>
        {
            receivedAudio = audio;
            tcs.TrySetResult();
        };

        var expectedBytes = new byte[] { 0xAA, 0xBB, 0xCC };
        var audioEvent = JsonSerializer.Serialize(new
        {
            type = "response.audio.delta",
            delta = Convert.ToBase64String(expectedBytes)
        });

        await _connection.EnqueueServerEventAsync(audioEvent);
        await WaitWithTimeout(tcs.Task);

        Assert.NotNull(receivedAudio);
        Assert.Equal(expectedBytes, receivedAudio!.Value.ToArray());
    }

    // --- AC3 continued: TranscriptReceived fires ---

    [Fact]
    public async Task TranscriptReceived_FiresOnTranscriptDelta()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);

        string? receivedText = null;
        var tcs = new TaskCompletionSource();
        session.TranscriptReceived += text =>
        {
            receivedText = text;
            tcs.TrySetResult();
        };

        var transcriptEvent = JsonSerializer.Serialize(new
        {
            type = "response.audio_transcript.delta",
            delta = "Hello, how can I help?"
        });

        await _connection.EnqueueServerEventAsync(transcriptEvent);
        await WaitWithTimeout(tcs.Task);

        Assert.Equal("Hello, how can I help?", receivedText);
    }

    // --- AC4: FunctionCallReceived fires and dispatches ---

    [Fact]
    public async Task FunctionCallReceived_FiresOnFunctionCallDone()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);

        FunctionCall? receivedCall = null;
        var tcs = new TaskCompletionSource();
        session.FunctionCallReceived += call =>
        {
            receivedCall = call;
            tcs.TrySetResult();
        };

        var fcEvent = JsonSerializer.Serialize(new
        {
            type = "response.function_call_arguments.done",
            call_id = "call_123",
            name = "send_to_cli",
            arguments = """{"prompt":"git status"}"""
        });

        await _connection.EnqueueServerEventAsync(fcEvent);
        await WaitWithTimeout(tcs.Task);

        Assert.NotNull(receivedCall);
        Assert.Equal("call_123", receivedCall!.CallId);
        Assert.Equal("send_to_cli", receivedCall.Name);
        Assert.Contains("git status", receivedCall.Arguments);
    }

    [Fact]
    public async Task SendFunctionResultAsync_SendsCorrectEvent()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);
        await _connection.ReadClientEventAsync(); // drain session.update

        await session.SendFunctionResultAsync("call_123", """{"status":"ok"}""");

        // Should send conversation.item.create
        var sentJson = await _connection.ReadClientEventAsync();
        var doc = JsonDocument.Parse(sentJson);
        Assert.Equal("conversation.item.create", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("call_123", doc.RootElement.GetProperty("item").GetProperty("call_id").GetString());

        // Should also send response.create
        var responseJson = await _connection.ReadClientEventAsync();
        var responseDoc = JsonDocument.Parse(responseJson);
        Assert.Equal("response.create", responseDoc.RootElement.GetProperty("type").GetString());
    }

    // --- AC5: Session configuration ---

    [Fact]
    public async Task UpdateSessionAsync_SendsSessionUpdateEvent()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);
        await _connection.ReadClientEventAsync(); // drain initial session.update

        var update = new SessionUpdate(
            Modalities: new[] { "text" },
            Voice: "echo",
            Instructions: "Updated instructions",
            Tools: new[] { new ToolDefinition("test_tool", "A test tool", """{"type":"object","properties":{}}""") }
        );

        await session.UpdateSessionAsync(update);

        var sentJson = await _connection.ReadClientEventAsync();
        var doc = JsonDocument.Parse(sentJson);

        Assert.Equal("session.update", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("echo", doc.RootElement.GetProperty("session").GetProperty("voice").GetString());
    }

    // --- AC5 continued: SessionReady fires ---

    [Fact]
    public async Task SessionReady_FiresOnSessionCreated()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);

        var tcs = new TaskCompletionSource();
        session.SessionReady += () => tcs.TrySetResult();

        var sessionEvent = JsonSerializer.Serialize(new { type = "session.created" });
        await _connection.EnqueueServerEventAsync(sessionEvent);
        await WaitWithTimeout(tcs.Task);

        // If we get here without timeout, the event fired
    }

    [Fact]
    public async Task SessionReady_FiresOnSessionUpdated()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);

        var tcs = new TaskCompletionSource();
        session.SessionReady += () => tcs.TrySetResult();

        var sessionEvent = JsonSerializer.Serialize(new { type = "session.updated" });
        await _connection.EnqueueServerEventAsync(sessionEvent);
        await WaitWithTimeout(tcs.Task);
    }

    // --- Error handling ---

    [Fact]
    public async Task ErrorReceived_FiresOnError()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);

        string? receivedError = null;
        var tcs = new TaskCompletionSource();
        session.ErrorReceived += error =>
        {
            receivedError = error;
            tcs.TrySetResult();
        };

        var errorEvent = JsonSerializer.Serialize(new
        {
            type = "error",
            error = new { message = "Rate limit exceeded", code = "rate_limit" }
        });

        await _connection.EnqueueServerEventAsync(errorEvent);
        await WaitWithTimeout(tcs.Task);

        Assert.Equal("Rate limit exceeded", receivedError);
    }

    // --- AC6: Disconnection fires event ---

    [Fact]
    public async Task Disconnected_FiresOnConnectionClose()
    {
        // Use a factory that throws to make reconnection fail instantly
        var failingFactory = () =>
        {
            var fake = new FakeRealtimeConnection();
            // Make the fake's ConnectAsync work, but the server stream is immediately complete
            return (IRealtimeConnection)new FailingRealtimeConnection();
        };

        _session = new VoiceLiveSession(_config, _connection, failingFactory);
        _session.DelayFunc = (_, _) => Task.CompletedTask; // Skip real delays in test
        var session = _session;

        await session.StartAsync(CancellationToken.None);

        var tcs = new TaskCompletionSource();
        session.Disconnected += () => tcs.TrySetResult();

        // Simulate server closing connection
        _connection.CompleteServerStream();

        // The session should fire Disconnected after exhausting reconnection attempts
        await WaitWithTimeout(tcs.Task, TimeSpan.FromSeconds(5));
    }

    // --- AC7: DisposeAsync closes gracefully ---

    [Fact]
    public async Task DisposeAsync_ClosesGracefully()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);

        // Dispose should not throw
        _connection.CompleteServerStream();
        await session.DisposeAsync();
        _session = null; // prevent double-dispose in DisposeAsync
    }

    [Fact]
    public async Task DisposeAsync_ThrowsOnSubsequentCalls()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);
        _connection.CompleteServerStream();
        await session.DisposeAsync();
        _session = null;

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            session.SendAudioAsync(new byte[] { 0x01 }));
    }

    // --- SendTextAsync ---

    [Fact]
    public async Task SendTextAsync_SendsConversationItemCreate()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);
        await _connection.ReadClientEventAsync(); // drain session.update

        await session.SendTextAsync("Hello from text");

        var sentJson = await _connection.ReadClientEventAsync();
        var doc = JsonDocument.Parse(sentJson);

        Assert.Equal("conversation.item.create", doc.RootElement.GetProperty("type").GetString());

        // Should also send response.create
        var responseJson = await _connection.ReadClientEventAsync();
        var responseDoc = JsonDocument.Parse(responseJson);
        Assert.Equal("response.create", responseDoc.RootElement.GetProperty("type").GetString());
    }

    // --- URI and header building ---

    [Fact]
    public void BuildWebSocketUri_ConvertsHttpsToWss()
    {
        var config = new VoiceLiveConfig("https://test.openai.azure.com", Model: "gpt-4o-realtime");
        var uri = VoiceLiveSession.BuildWebSocketUri(config);

        Assert.StartsWith("wss://", uri.ToString());
        Assert.Contains("test.openai.azure.com", uri.ToString());
        Assert.Contains("gpt-4o-realtime", uri.ToString());
    }

    [Fact]
    public void BuildWebSocketUri_HandlesWssPrefix()
    {
        var config = new VoiceLiveConfig("wss://test.openai.azure.com", Model: "gpt-4o-realtime");
        var uri = VoiceLiveSession.BuildWebSocketUri(config);

        Assert.StartsWith("wss://", uri.ToString());
        Assert.DoesNotContain("wss://wss://", uri.ToString());
    }

    [Fact]
    public void BuildHeaders_IncludesApiKey()
    {
        var config = new VoiceLiveConfig("https://test.com", ApiKey: "my-key");
        var headers = VoiceLiveSession.BuildHeaders(config);

        Assert.Equal("my-key", headers["api-key"]);
    }

    [Fact]
    public void BuildHeaders_OmitsApiKeyWhenNull()
    {
        var config = new VoiceLiveConfig("https://test.com", ApiKey: null);
        var headers = VoiceLiveSession.BuildHeaders(config);

        Assert.Empty(headers);
    }

    // --- Event dispatch for malformed JSON ---

    [Fact]
    public async Task DispatchEvent_HandlesInvalidJson()
    {
        var session = CreateSession();
        await session.StartAsync(CancellationToken.None);

        string? receivedError = null;
        var tcs = new TaskCompletionSource();
        session.ErrorReceived += error =>
        {
            receivedError = error;
            tcs.TrySetResult();
        };

        await _connection.EnqueueServerEventAsync("not valid json {{{");
        await WaitWithTimeout(tcs.Task);

        Assert.Contains("Failed to parse", receivedError);
    }

    [Fact]
    public void DispatchEvent_IgnoresUnknownEventTypes()
    {
        var session = CreateSession();

        // Should not throw
        session.DispatchEvent(JsonSerializer.Serialize(new { type = "response.done" }));
        session.DispatchEvent(JsonSerializer.Serialize(new { type = "input_audio_buffer.speech_started" }));
    }

    // --- Helper ---

    private static async Task WaitWithTimeout(Task task, TimeSpan? timeout = null)
    {
        var delay = Task.Delay(timeout ?? TimeSpan.FromSeconds(3));
        if (await Task.WhenAny(task, delay) != task)
            throw new TimeoutException("Event was not received within the timeout period.");
    }
}

/// <summary>
/// A connection that always fails on ConnectAsync — used to test reconnection failure paths.
/// </summary>
internal sealed class FailingRealtimeConnection : IRealtimeConnection
{
    public bool IsConnected => false;

    public Task ConnectAsync(Uri uri, IDictionary<string, string> headers, CancellationToken ct)
        => throw new InvalidOperationException("Simulated connection failure");

    public Task SendEventAsync(string eventJson, CancellationToken ct)
        => throw new InvalidOperationException("Not connected");

    public async IAsyncEnumerable<string> ReceiveEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        yield break;
    }

    public Task CloseAsync(CancellationToken ct) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
