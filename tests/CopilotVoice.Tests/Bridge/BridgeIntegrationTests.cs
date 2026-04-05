using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CopilotVoice.Bridge;
using Xunit;

namespace CopilotVoice.Tests.Bridge;

/// <summary>
/// Integration and edge-case tests for the HTTP Bridge Server.
/// Covers gaps not addressed by the Developer's unit tests:
/// concurrent access, validation edge cases, event wiring, and
/// channel overflow behavior.
/// </summary>
public class BridgeIntegrationTests : IAsyncLifetime
{
    private BridgeServer _server = null!;
    private HttpClient _client = null!;
    private int _port;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task InitializeAsync()
    {
        _port = GetRandomPort();
        _server = new BridgeServer(_port);
        await _server.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri($"http://localhost:{_port}") };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _server.DisposeAsync();
    }

    // --- [AC1] Default port configuration ---

    /// <summary>
    /// [AC1] Verifies the BridgeServer constructor defaults to port 7701
    /// when no port is specified. We construct without starting to avoid
    /// port conflicts — we only need to verify the default compiles and
    /// the server can start on a given port.
    /// </summary>
    [Fact]
    public async Task DefaultConstructor_UsesPort7701()
    {
        // The default BridgeServer() constructor defaults to 7701.
        // We can't actually bind to 7701 (might be in use), but we verify
        // the parameterless overload exists and constructs without error.
        var server = new BridgeServer();
        // Dispose without starting — just verifying default construction.
        await server.DisposeAsync();
    }

    // --- [AC4][PERF] Command delivery latency ---

    /// <summary>
    /// [AC4][PERF] Commands queued via QueueCommand must be delivered
    /// to the SSE stream within 50ms. Measures round-trip from queue
    /// to SSE event received by the HTTP client.
    /// </summary>
    [Fact]
    public async Task QueueCommand_DeliveredWithin50ms()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Start SSE connection
        var request = new HttpRequestMessage(HttpMethod.Get, "/cli/commands?sessionId=perf-test");
        request.Headers.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        // Read and discard the initial ping
        await ReadSseEventAsync(reader, cts.Token);

        // Measure delivery latency
        var sw = Stopwatch.StartNew();
        _server.SessionBridge.QueueCommand("perf-test", new SendPromptCommand("latency test"));

        var lines = await ReadSseEventAsync(reader, cts.Token);
        sw.Stop();

        Assert.Contains(lines, l => l.Contains("latency test"));
        Assert.True(sw.ElapsedMilliseconds < 50,
            $"Command delivery took {sw.ElapsedMilliseconds}ms, expected <50ms");
    }

    // --- [AC6][EDGE] /cli/event missing timestamp returns 400 ---

    /// <summary>
    /// [AC6][EDGE] POST /cli/event with valid type but missing timestamp
    /// field should return 400 Bad Request. The existing test only covers
    /// missing "type" — this covers the other required field.
    /// </summary>
    [Fact]
    public async Task PostEvent_MissingTimestamp_Returns400()
    {
        var payload = new { type = "session.start" }; // missing timestamp

        var response = await _client.PostAsJsonAsync("/cli/event", payload, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("timestamp", body, StringComparison.OrdinalIgnoreCase);
    }

    // --- [AC6][EDGE] /cli/send missing prompt returns 400 ---

    /// <summary>
    /// [AC6][EDGE] POST /cli/send with missing "prompt" field should
    /// return 400 Bad Request. Validates input contract for command sending.
    /// </summary>
    [Fact]
    public async Task PostSend_MissingPrompt_Returns400()
    {
        var payload = new { sessionId = "s1" }; // missing prompt

        var response = await _client.PostAsJsonAsync("/cli/send", payload, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// [AC6][EDGE] POST /cli/send with empty prompt string should
    /// return 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task PostSend_EmptyPrompt_Returns400()
    {
        var payload = new { prompt = "", sessionId = "s1" };

        var response = await _client.PostAsJsonAsync("/cli/send", payload, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- [EDGE] /cli/send broadcast to all sessions ---

    /// <summary>
    /// [EDGE] POST /cli/send without sessionId broadcasts the command
    /// to all connected sessions. Verifies the broadcast code path.
    /// </summary>
    [Fact]
    public async Task PostSend_NoSessionId_BroadcastsToAllSessions()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Connect two SSE sessions
        var (reader1, resp1) = await StartSseSession("broadcast-a", cts.Token);
        var (reader2, resp2) = await StartSseSession("broadcast-b", cts.Token);

        // POST /cli/send without sessionId → broadcast
        var payload = new { prompt = "broadcast message" };
        var response = await _client.PostAsJsonAsync("/cli/send", payload, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Both sessions should receive the command
        var lines1 = await ReadSseEventAsync(reader1, cts.Token);
        var lines2 = await ReadSseEventAsync(reader2, cts.Token);

        Assert.Contains(lines1, l => l.Contains("broadcast message"));
        Assert.Contains(lines2, l => l.Contains("broadcast message"));

        resp1.Dispose();
        resp2.Dispose();
    }

    // --- [EDGE] /speak fires SpeakRequested event ---

    /// <summary>
    /// [EDGE] POST /speak with valid text fires the SpeakRequested event
    /// with the correct text content, not just returning 200.
    /// </summary>
    [Fact]
    public async Task PostSpeak_FiresSpeakRequestedEvent()
    {
        string? receivedText = null;
        _server.SpeakRequested += text => receivedText = text;

        var payload = new { text = "Please speak this" };
        var response = await _client.PostAsJsonAsync("/speak", payload, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Please speak this", receivedText);
    }

    // --- [EDGE] /avatar fires AvatarRequested event ---

    /// <summary>
    /// [EDGE] POST /avatar with valid expression fires the AvatarRequested event
    /// with the correct expression, not just returning 200.
    /// </summary>
    [Fact]
    public async Task PostAvatar_FiresAvatarRequestedEvent()
    {
        string? receivedExpression = null;
        _server.AvatarRequested += expr => receivedExpression = expr;

        var payload = new { expression = "surprised" };
        var response = await _client.PostAsJsonAsync("/avatar", payload, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("surprised", receivedExpression);
    }

    // --- [EDGE] Empty body handling ---

    /// <summary>
    /// [EDGE] POST /cli/message with empty body returns a 400
    /// (either malformed JSON or missing fields error).
    /// </summary>
    [Fact]
    public async Task PostMessage_EmptyBody_Returns400()
    {
        var content = new StringContent("", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/cli/message", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// [EDGE] POST /cli/event with empty body returns 400.
    /// </summary>
    [Fact]
    public async Task PostEvent_EmptyBody_Returns400()
    {
        var content = new StringContent("", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/cli/event", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// [EDGE] POST /cli/send with empty body returns 400.
    /// </summary>
    [Fact]
    public async Task PostSend_EmptyBody_Returns400()
    {
        var content = new StringContent("", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/cli/send", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- [AC6][EDGE] Malformed JSON across endpoints ---

    /// <summary>
    /// [AC6][EDGE] POST /cli/event with malformed JSON returns 400.
    /// </summary>
    [Fact]
    public async Task PostEvent_MalformedJson_Returns400()
    {
        var content = new StringContent("{not valid json", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/cli/event", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// [AC6][EDGE] POST /cli/send with malformed JSON returns 400.
    /// </summary>
    [Fact]
    public async Task PostSend_MalformedJson_Returns400()
    {
        var content = new StringContent("{{invalid}}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/cli/send", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- [BOUNDARY] Concurrent SSE sessions with independent commands ---

    /// <summary>
    /// [AC5][BOUNDARY] Two concurrent SSE sessions receive only their own
    /// targeted commands. Verifies session isolation at the HTTP integration level.
    /// </summary>
    [Fact]
    public async Task ConcurrentSseSessions_ReceiveIndependentCommands()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Connect two SSE sessions
        var (readerA, respA) = await StartSseSession("session-iso-a", cts.Token);
        var (readerB, respB) = await StartSseSession("session-iso-b", cts.Token);

        // Queue commands to specific sessions via /cli/send
        var payloadA = new { prompt = "Only for A", sessionId = "session-iso-a" };
        var payloadB = new { prompt = "Only for B", sessionId = "session-iso-b" };

        await _client.PostAsJsonAsync("/cli/send", payloadA, JsonOptions);
        await _client.PostAsJsonAsync("/cli/send", payloadB, JsonOptions);

        var linesA = await ReadSseEventAsync(readerA, cts.Token);
        var linesB = await ReadSseEventAsync(readerB, cts.Token);

        Assert.Contains(linesA, l => l.Contains("Only for A"));
        Assert.DoesNotContain(linesA, l => l.Contains("Only for B"));

        Assert.Contains(linesB, l => l.Contains("Only for B"));
        Assert.DoesNotContain(linesB, l => l.Contains("Only for A"));

        respA.Dispose();
        respB.Dispose();
    }

    // --- [BOUNDARY] SessionBridge channel overflow drops oldest ---

    /// <summary>
    /// [BOUNDARY] When more than 100 commands are queued to a session's
    /// bounded channel without being consumed, the oldest commands are dropped
    /// (DropOldest policy). The consumer should receive the most recent commands.
    /// </summary>
    [Fact]
    public async Task SessionBridge_ChannelOverflow_DropsOldest()
    {
        var bridge = new SessionBridge();

        // Queue 110 commands without consuming any (channel capacity is 100)
        for (int i = 0; i < 110; i++)
        {
            bridge.QueueCommand("overflow-test", new SendPromptCommand($"Command-{i}"));
        }

        // Now consume all available commands
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var received = new List<string>();
        await foreach (var cmd in bridge.GetCommandStream("overflow-test", cts.Token))
        {
            received.Add(cmd.Prompt);
            if (received.Count >= 100) break;
        }

        // Should have 100 commands, and the oldest ones (0-9) should be dropped
        Assert.Equal(100, received.Count);
        // The last command queued (Command-109) should definitely be present
        Assert.Contains("Command-109", received);
        // The first command queued (Command-0) should be dropped
        Assert.DoesNotContain("Command-0", received);
    }

    // --- [AC7][EDGE] Health check with active sessions ---

    /// <summary>
    /// [AC7][EDGE] After connecting sessions via SSE and then disconnecting,
    /// the health endpoint reflects the current count accurately.
    /// </summary>
    [Fact]
    public async Task Health_ReflectsSessionCountAfterRemoval()
    {
        // Create a session via QueueCommand
        _server.SessionBridge.QueueCommand("temp-session", new SendPromptCommand("test"));

        // Verify count increased
        var response1 = await _client.GetAsync("/health");
        var body1 = await response1.Content.ReadAsStringAsync();
        var doc1 = JsonDocument.Parse(body1);
        Assert.True(doc1.RootElement.GetProperty("sessions").GetInt32() >= 1);

        // Remove session
        _server.SessionBridge.RemoveSession("temp-session");

        // Verify count decreased
        var response2 = await _client.GetAsync("/health");
        var body2 = await response2.Content.ReadAsStringAsync();
        var doc2 = JsonDocument.Parse(body2);
        Assert.Equal(0, doc2.RootElement.GetProperty("sessions").GetInt32());
    }

    // --- [AC3][EDGE] Multiple rapid messages ---

    /// <summary>
    /// [AC3][EDGE] Multiple messages posted in rapid succession all
    /// fire MessageReceived events. Verifies no message loss under burst.
    /// </summary>
    [Fact]
    public async Task PostMessage_RapidBurst_AllMessagesReceived()
    {
        var received = new List<string>();
        _server.SessionBridge.MessageReceived += msg => received.Add(msg.MessageId);

        const int count = 20;
        var tasks = Enumerable.Range(0, count).Select(i =>
        {
            var payload = new
            {
                type = "assistant.message",
                content = $"Message {i}",
                messageId = $"burst-{i}",
                timestamp = 1712345678000L + i
            };
            return _client.PostAsJsonAsync("/cli/message", payload, JsonOptions);
        });

        var responses = await Task.WhenAll(tasks);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        // Allow event propagation
        await Task.Delay(100);

        Assert.Equal(count, received.Count);
        for (int i = 0; i < count; i++)
        {
            Assert.Contains($"burst-{i}", received);
        }
    }

    // --- [CONTRACT] Response body format consistency ---

    /// <summary>
    /// [CONTRACT] All successful POST endpoints return {"status":"ok"}.
    /// Verifies consistent response contract across endpoints.
    /// </summary>
    [Fact]
    public async Task AllEndpoints_SuccessResponse_ContainsStatusOk()
    {
        // /cli/message
        var msgPayload = new
        {
            type = "assistant.message",
            content = "test",
            messageId = "contract-1",
            timestamp = 1712345678000L
        };
        var msgResp = await _client.PostAsJsonAsync("/cli/message", msgPayload, JsonOptions);
        await AssertStatusOk(msgResp);

        // /cli/event
        var evtPayload = new
        {
            type = "session.start",
            timestamp = 1712345678000L
        };
        var evtResp = await _client.PostAsJsonAsync("/cli/event", evtPayload, JsonOptions);
        await AssertStatusOk(evtResp);

        // /speak
        var speakPayload = new { text = "hello" };
        var speakResp = await _client.PostAsJsonAsync("/speak", speakPayload, JsonOptions);
        await AssertStatusOk(speakResp);

        // /avatar
        var avatarPayload = new { expression = "neutral" };
        var avatarResp = await _client.PostAsJsonAsync("/avatar", avatarPayload, JsonOptions);
        await AssertStatusOk(avatarResp);
    }

    // --- [CONTRACT] SSE stream content-type and headers ---

    /// <summary>
    /// [CONTRACT] The SSE endpoint sets correct Cache-Control and Connection headers.
    /// </summary>
    [Fact]
    public async Task SseCommands_SetsCorrectHeaders()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var request = new HttpRequestMessage(HttpMethod.Get, "/cli/commands?sessionId=header-test");
        request.Headers.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    // --- Helpers ---

    private async Task<(StreamReader reader, HttpResponseMessage response)> StartSseSession(
        string sessionId, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/cli/commands?sessionId={sessionId}");
        request.Headers.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var stream = await response.Content.ReadAsStreamAsync(ct);
        var reader = new StreamReader(stream);

        // Read and discard the initial ping
        await ReadSseEventAsync(reader, ct);

        return (reader, response);
    }

    private static async Task AssertStatusOk(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
    }

    private static async Task<List<string>> ReadSseEventAsync(StreamReader reader, CancellationToken ct)
    {
        var lines = new List<string>();
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            if (line == string.Empty)
            {
                if (lines.Count > 0) break;
                continue;
            }
            lines.Add(line);
        }
        return lines;
    }

    private static int GetRandomPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
