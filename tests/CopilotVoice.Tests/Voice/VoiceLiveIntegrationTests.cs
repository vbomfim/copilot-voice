using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CopilotVoice.Voice;
using Xunit;

namespace CopilotVoice.Tests.Voice;

/// <summary>
/// Integration and edge-case tests for the Voice Live API client.
/// Covers reconnection success path, exponential backoff verification,
/// auth failure abort, event dispatch edge cases, and full function-call
/// round-trip integration.
/// </summary>
public class VoiceLiveIntegrationTests : IAsyncDisposable
{
    private readonly VoiceLiveConfig _config = new(
        Endpoint: "https://test.openai.azure.com",
        ApiKey: "test-key",
        Model: "gpt-4o-realtime-preview",
        Voice: "alloy",
        SystemInstructions: "You are a test assistant."
    );

    private VoiceLiveSession? _session;

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }
    }

    // === AC6: Reconnection — Success Path ===

    /// <summary>
    /// [AC6][COVERAGE] When the WebSocket drops, the session reconnects
    /// and continues dispatching events from the new connection.
    /// This is the most critical gap — existing tests only cover the
    /// failure path (Disconnected fires after exhausting retries).
    /// </summary>
    [Fact]
    public async Task Reconnection_SucceedsAfterTransientFailure()
    {
        // Connection 1: will drop after first event
        var conn1 = new FakeRealtimeConnection();
        // Connection 2: will be the successful reconnection
        var conn2 = new FakeRealtimeConnection();

        var connectionQueue = new Queue<IRealtimeConnection>();
        connectionQueue.Enqueue(conn2);

        _session = new VoiceLiveSession(_config, conn1, () =>
        {
            if (connectionQueue.Count > 0)
                return connectionQueue.Dequeue();
            return new FakeRealtimeConnection();
        });
        _session.DelayFunc = (_, _) => Task.CompletedTask; // Skip real delays

        await _session.StartAsync(CancellationToken.None);

        // Set up event capture on the second connection
        string? receivedTranscript = null;
        var tcs = new TaskCompletionSource();
        _session.TranscriptReceived += text =>
        {
            receivedTranscript = text;
            tcs.TrySetResult();
        };

        // Simulate server closing connection 1 — triggers reconnection
        conn1.CompleteServerStream();

        // Wait a moment for reconnection to happen
        await Task.Delay(200);

        // Connection 2 should be active — send an event through it
        var transcriptEvent = JsonSerializer.Serialize(new
        {
            type = "response.audio_transcript.delta",
            delta = "Reconnected successfully"
        });
        await conn2.EnqueueServerEventAsync(transcriptEvent);

        await WaitWithTimeout(tcs.Task, TimeSpan.FromSeconds(5));

        Assert.Equal("Reconnected successfully", receivedTranscript);

        // Clean up
        conn2.CompleteServerStream();
    }

    /// <summary>
    /// [AC6][COVERAGE] After reconnection, the session sends a new
    /// session.update event to reconfigure the API with original settings.
    /// </summary>
    [Fact]
    public async Task Reconnection_SendsNewSessionUpdate()
    {
        var conn1 = new FakeRealtimeConnection();
        var conn2 = new FakeRealtimeConnection();

        _session = new VoiceLiveSession(_config, conn1, () => conn2);
        _session.DelayFunc = (_, _) => Task.CompletedTask;

        await _session.StartAsync(CancellationToken.None);

        // Drain the initial session.update from conn1
        await conn1.ReadClientEventAsync();

        // Trigger reconnection
        conn1.CompleteServerStream();

        // Wait for reconnection
        await Task.Delay(200);

        // conn2 should have received a session.update
        var sentJson = await conn2.ReadClientEventAsync(TimeSpan.FromSeconds(3));
        var doc = JsonDocument.Parse(sentJson);

        Assert.Equal("session.update", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("alloy",
            doc.RootElement.GetProperty("session").GetProperty("voice").GetString());

        conn2.CompleteServerStream();
    }

    // === AC6: Exponential Backoff ===

    /// <summary>
    /// [AC6][COVERAGE] Reconnection attempts use exponential backoff.
    /// The internal ReconnectAttempted event provides (attempt, delay) tuples.
    /// Verifies that delays follow the pattern: 1s, 2s, 4s, 8s, 16s, 30s (capped).
    /// </summary>
    [Fact]
    public async Task Reconnection_ExponentialBackoff_Verified()
    {
        var conn = new FakeRealtimeConnection();

        var attempts = new List<(int Attempt, TimeSpan Delay)>();

        _session = new VoiceLiveSession(_config, conn, () => new FailingRealtimeConnection());
        _session.DelayFunc = (_, _) => Task.CompletedTask; // Skip real delays
        _session.ReconnectAttempted += (attempt, delay) =>
        {
            attempts.Add((attempt, delay));
        };

        var disconnected = new TaskCompletionSource();
        _session.Disconnected += () => disconnected.TrySetResult();

        await _session.StartAsync(CancellationToken.None);

        // Trigger disconnection — all reconnects will fail → Disconnected fires
        conn.CompleteServerStream();
        await WaitWithTimeout(disconnected.Task, TimeSpan.FromSeconds(10));

        // Should have exactly 10 attempts (MaxReconnectAttempts = 10)
        Assert.Equal(10, attempts.Count);

        // Verify exponential backoff pattern
        Assert.Equal(TimeSpan.FromSeconds(1), attempts[0].Delay);   // 2^0 = 1
        Assert.Equal(TimeSpan.FromSeconds(2), attempts[1].Delay);   // 2^1 = 2
        Assert.Equal(TimeSpan.FromSeconds(4), attempts[2].Delay);   // 2^2 = 4
        Assert.Equal(TimeSpan.FromSeconds(8), attempts[3].Delay);   // 2^3 = 8
        Assert.Equal(TimeSpan.FromSeconds(16), attempts[4].Delay);  // 2^4 = 16
        Assert.Equal(TimeSpan.FromSeconds(30), attempts[5].Delay);  // capped at 30
        Assert.Equal(TimeSpan.FromSeconds(30), attempts[9].Delay);  // still capped
    }

    // === AC6: Auth failure during reconnection ===

    /// <summary>
    /// [AC6][EDGE] When reconnection hits a 401/403 auth error,
    /// retries stop immediately and ErrorReceived fires with an auth message.
    /// </summary>
    [Fact]
    public async Task Reconnection_AuthFailure_StopsRetrying()
    {
        var conn = new FakeRealtimeConnection();

        string? receivedError = null;
        var errorTcs = new TaskCompletionSource();
        var disconnectedTcs = new TaskCompletionSource();
        var reconnectCount = 0;

        _session = new VoiceLiveSession(_config, conn, () => new AuthFailingConnection());
        _session.DelayFunc = (_, _) => Task.CompletedTask;
        _session.ErrorReceived += error =>
        {
            receivedError = error;
            errorTcs.TrySetResult();
        };
        _session.Disconnected += () => disconnectedTcs.TrySetResult();
        _session.ReconnectAttempted += (_, _) => Interlocked.Increment(ref reconnectCount);

        await _session.StartAsync(CancellationToken.None);
        conn.CompleteServerStream();

        await WaitWithTimeout(disconnectedTcs.Task, TimeSpan.FromSeconds(5));

        // Auth failure should have stopped retries after first attempt
        Assert.Equal(1, reconnectCount);
        Assert.NotNull(receivedError);
        Assert.Contains("Authentication failed", receivedError!);
    }

    // === Event Dispatch Edge Cases ===

    /// <summary>
    /// [EDGE] DispatchEvent with a JSON object missing the "type" property
    /// should be silently ignored — no error, no event fired.
    /// </summary>
    [Fact]
    public async Task DispatchEvent_MissingTypeProperty_SilentlyIgnored()
    {
        var conn = new FakeRealtimeConnection();
        _session = new VoiceLiveSession(_config, conn, () => new FakeRealtimeConnection());

        var errorFired = false;
        _session.ErrorReceived += _ => errorFired = true;

        // Dispatch a JSON event with no "type" field
        _session.DispatchEvent("""{"data":"no type here"}""");

        Assert.False(errorFired, "No error should fire for valid JSON without 'type'");
    }

    /// <summary>
    /// [EDGE] DispatchEvent with an error event that has no "message" property
    /// should fire ErrorReceived with "Unknown error".
    /// </summary>
    [Fact]
    public async Task DispatchEvent_ErrorWithNoMessage_ReturnsUnknownError()
    {
        var conn = new FakeRealtimeConnection();
        _session = new VoiceLiveSession(_config, conn, () => new FakeRealtimeConnection());

        string? receivedError = null;
        _session.ErrorReceived += error => receivedError = error;

        // Error event with no "error.message" property
        _session.DispatchEvent("""{"type":"error"}""");

        Assert.Equal("Unknown error", receivedError);
    }

    /// <summary>
    /// [EDGE] DispatchEvent with error object but missing message sub-property
    /// should fire ErrorReceived with "Unknown error".
    /// </summary>
    [Fact]
    public void DispatchEvent_ErrorObjectWithoutMessage_ReturnsUnknownError()
    {
        var conn = new FakeRealtimeConnection();
        _session = new VoiceLiveSession(_config, conn, () => new FakeRealtimeConnection());

        string? receivedError = null;
        _session.ErrorReceived += error => receivedError = error;

        _session.DispatchEvent("""{"type":"error","error":{"code":"server_error"}}""");

        Assert.Equal("Unknown error", receivedError);
    }

    // === Audio Edge Cases ===

    /// <summary>
    /// [EDGE] SendAudioAsync with an empty byte array sends a valid
    /// input_audio_buffer.append event with an empty base64 audio field.
    /// The API should accept it (no crash, no assertion failure).
    /// </summary>
    [Fact]
    public async Task SendAudioAsync_EmptyBuffer_SendsValidEvent()
    {
        var conn = new FakeRealtimeConnection();
        _session = new VoiceLiveSession(_config, conn, () => new FakeRealtimeConnection());
        await _session.StartAsync(CancellationToken.None);
        await conn.ReadClientEventAsync(); // drain session.update

        await _session.SendAudioAsync(ReadOnlyMemory<byte>.Empty);

        var sentJson = await conn.ReadClientEventAsync();
        var doc = JsonDocument.Parse(sentJson);

        Assert.Equal("input_audio_buffer.append", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("", doc.RootElement.GetProperty("audio").GetString());

        conn.CompleteServerStream();
    }

    /// <summary>
    /// [BOUNDARY] Multiple consecutive audio chunks are all received
    /// and dispatched independently. Verifies no event coalescing or loss.
    /// </summary>
    [Fact]
    public async Task MultipleAudioChunks_AllReceivedIndependently()
    {
        var conn = new FakeRealtimeConnection();
        _session = new VoiceLiveSession(_config, conn, () => new FakeRealtimeConnection());
        await _session.StartAsync(CancellationToken.None);

        var receivedChunks = new List<byte[]>();
        var allReceived = new TaskCompletionSource();
        _session.AudioReceived += audio =>
        {
            lock (receivedChunks)
            {
                receivedChunks.Add(audio.ToArray());
                if (receivedChunks.Count == 3)
                    allReceived.TrySetResult();
            }
        };

        // Send 3 audio chunks
        for (int i = 0; i < 3; i++)
        {
            var chunk = new byte[] { (byte)(0x10 + i), (byte)(0x20 + i) };
            var audioEvent = JsonSerializer.Serialize(new
            {
                type = "response.audio.delta",
                delta = Convert.ToBase64String(chunk)
            });
            await conn.EnqueueServerEventAsync(audioEvent);
        }

        await WaitWithTimeout(allReceived.Task);

        Assert.Equal(3, receivedChunks.Count);
        Assert.Equal(new byte[] { 0x10, 0x20 }, receivedChunks[0]);
        Assert.Equal(new byte[] { 0x11, 0x21 }, receivedChunks[1]);
        Assert.Equal(new byte[] { 0x12, 0x22 }, receivedChunks[2]);

        conn.CompleteServerStream();
    }

    // === Full Function Call Round-Trip Integration ===

    /// <summary>
    /// [AC4][COVERAGE] Integration test: FunctionCallReceived fires →
    /// FunctionCallHandler dispatches → SendFunctionResultAsync sends result.
    /// Tests the full round-trip that the developer tested individually.
    /// </summary>
    [Fact]
    public async Task FunctionCall_FullRoundTrip_Integration()
    {
        var conn = new FakeRealtimeConnection();
        _session = new VoiceLiveSession(_config, conn, () => new FakeRealtimeConnection());
        await _session.StartAsync(CancellationToken.None);
        await conn.ReadClientEventAsync(); // drain session.update

        var handler = new FunctionCallHandler();
        var roundTripComplete = new TaskCompletionSource();

        // Wire up: when a function call is received, handle it and send result back
        _session.FunctionCallReceived += async call =>
        {
            try
            {
                var result = await handler.HandleAsync(call);
                await _session.SendFunctionResultAsync(call.CallId, result);
                roundTripComplete.TrySetResult();
            }
            catch (Exception ex)
            {
                roundTripComplete.TrySetException(ex);
            }
        };

        // Simulate the API requesting a function call
        var fcEvent = JsonSerializer.Serialize(new
        {
            type = "response.function_call_arguments.done",
            call_id = "call_rt_1",
            name = "get_session_context",
            arguments = "{}"
        });
        await conn.EnqueueServerEventAsync(fcEvent);

        await WaitWithTimeout(roundTripComplete.Task, TimeSpan.FromSeconds(5));

        // The session should have sent conversation.item.create with the result
        var sentJson = await conn.ReadClientEventAsync();
        var doc = JsonDocument.Parse(sentJson);
        Assert.Equal("conversation.item.create", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("call_rt_1",
            doc.RootElement.GetProperty("item").GetProperty("call_id").GetString());

        // And then response.create to continue
        var responseJson = await conn.ReadClientEventAsync();
        var responseDoc = JsonDocument.Parse(responseJson);
        Assert.Equal("response.create", responseDoc.RootElement.GetProperty("type").GetString());

        conn.CompleteServerStream();
    }

    // === Bridge Exception Handling ===

    /// <summary>
    /// [EDGE] When the session bridge throws during SendCommandAsync,
    /// the FunctionCallHandler returns an error result instead of crashing.
    /// </summary>
    [Fact]
    public async Task FunctionCallHandler_BridgeThrows_ReturnsError()
    {
        var bridge = new ThrowingSessionBridge();
        var handler = new FunctionCallHandler(sessionBridge: bridge);

        var call = new FunctionCall("c1", "send_to_cli", """{"prompt":"test"}""");
        var result = await handler.HandleAsync(call);
        var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.TryGetProperty("error", out var errorProp));
        Assert.Contains("Failed to send command", errorProp.GetString());
    }

    // === Dispose Edge Cases ===

    /// <summary>
    /// [AC7][EDGE] Calling DisposeAsync multiple times does not throw.
    /// Verifies idempotent disposal.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        var conn = new FakeRealtimeConnection();
        _session = new VoiceLiveSession(_config, conn, () => new FakeRealtimeConnection());
        await _session.StartAsync(CancellationToken.None);
        conn.CompleteServerStream();

        await _session.DisposeAsync();
        await _session.DisposeAsync(); // second call — should not throw
        _session = null; // prevent DisposeAsync() in cleanup
    }

    /// <summary>
    /// [AC7][EDGE] CommitAudioAsync throws ObjectDisposedException after disposal.
    /// </summary>
    [Fact]
    public async Task CommitAudioAsync_AfterDispose_Throws()
    {
        var conn = new FakeRealtimeConnection();
        _session = new VoiceLiveSession(_config, conn, () => new FakeRealtimeConnection());
        await _session.StartAsync(CancellationToken.None);
        conn.CompleteServerStream();
        await _session.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            _session.CommitAudioAsync());
        _session = null;
    }

    /// <summary>
    /// [AC7][EDGE] SendTextAsync throws ObjectDisposedException after disposal.
    /// </summary>
    [Fact]
    public async Task SendTextAsync_AfterDispose_Throws()
    {
        var conn = new FakeRealtimeConnection();
        _session = new VoiceLiveSession(_config, conn, () => new FakeRealtimeConnection());
        await _session.StartAsync(CancellationToken.None);
        conn.CompleteServerStream();
        await _session.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            _session.SendTextAsync("hello"));
        _session = null;
    }

    /// <summary>
    /// [AC7][EDGE] UpdateSessionAsync throws ObjectDisposedException after disposal.
    /// </summary>
    [Fact]
    public async Task UpdateSessionAsync_AfterDispose_Throws()
    {
        var conn = new FakeRealtimeConnection();
        _session = new VoiceLiveSession(_config, conn, () => new FakeRealtimeConnection());
        await _session.StartAsync(CancellationToken.None);
        conn.CompleteServerStream();
        await _session.DisposeAsync();

        var update = new SessionUpdate(new[] { "text" }, "echo", "test");
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            _session.UpdateSessionAsync(update));
        _session = null;
    }

    // === URI Building Edge Cases ===

    /// <summary>
    /// [EDGE] BuildWebSocketUri strips trailing slashes from endpoint.
    /// </summary>
    [Fact]
    public void BuildWebSocketUri_StripsTrailingSlash()
    {
        var config = new VoiceLiveConfig("https://test.openai.azure.com/");
        var uri = VoiceLiveSession.BuildWebSocketUri(config);

        Assert.DoesNotContain("//openai", uri.ToString());
        Assert.Contains("/openai/realtime", uri.ToString());
    }

    /// <summary>
    /// [EDGE] BuildWebSocketUri handles bare hostname (no scheme prefix).
    /// </summary>
    [Fact]
    public void BuildWebSocketUri_HandlesBareHostname()
    {
        var config = new VoiceLiveConfig("test.openai.azure.com");
        var uri = VoiceLiveSession.BuildWebSocketUri(config);

        Assert.StartsWith("wss://", uri.ToString());
        Assert.Contains("test.openai.azure.com", uri.ToString());
    }

    // === Helpers ===

    private static async Task WaitWithTimeout(Task task, TimeSpan? timeout = null)
    {
        var delay = Task.Delay(timeout ?? TimeSpan.FromSeconds(3));
        if (await Task.WhenAny(task, delay) != task)
            throw new TimeoutException("Event was not received within the timeout period.");
    }
}

// --- Test Doubles ---

/// <summary>
/// A connection that throws WebSocketException with "401" on ConnectAsync
/// to simulate authentication failure during reconnection.
/// </summary>
internal sealed class AuthFailingConnection : IRealtimeConnection
{
    public bool IsConnected => false;

    public Task ConnectAsync(Uri uri, IDictionary<string, string> headers, CancellationToken ct)
        => throw new WebSocketException("The server returned status code '401' (Unauthorized)");

    public Task SendEventAsync(string eventJson, CancellationToken ct)
        => throw new InvalidOperationException("Not connected");

    public async IAsyncEnumerable<string> ReceiveEventsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield break;
    }

    public Task CloseAsync(CancellationToken ct) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// A session bridge that throws on SendCommandAsync to test error handling.
/// </summary>
internal sealed class ThrowingSessionBridge : ICliBridgeClient
{
    public Task SendCommandAsync(string prompt, CancellationToken ct = default)
        => throw new InvalidOperationException("Bridge connection lost");

    public SessionBridgeState GetState() => new("error", null, "/test");
}
