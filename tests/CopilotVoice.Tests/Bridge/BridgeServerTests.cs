using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CopilotVoice.Bridge;
using Xunit;

namespace CopilotVoice.Tests.Bridge;

public class BridgeServerTests : IAsyncLifetime
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

    // --- Health Check (AC7) ---

    [Fact]
    public async Task Health_ReturnsOkWithSessionCount()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("sessions").GetInt32());
    }

    // --- POST /cli/message (AC3, AC6) ---

    [Fact]
    public async Task PostMessage_ValidPayload_Returns200AndFiresEvent()
    {
        CliMessage? received = null;
        _server.SessionBridge.MessageReceived += msg => received = msg;

        var payload = new
        {
            type = "assistant.message",
            content = "Hello from agent",
            messageId = "msg-123",
            timestamp = 1712345678000L,
            truncated = false
        };

        var response = await _client.PostAsJsonAsync("/cli/message", payload, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(received);
        Assert.Equal("Hello from agent", received!.Content);
        Assert.Equal("msg-123", received.MessageId);
        Assert.Equal("assistant.message", received.Type);
    }

    [Fact]
    public async Task PostMessage_MissingRequiredFields_Returns400()
    {
        var payload = new { content = "Hello" }; // missing type, messageId, timestamp

        var response = await _client.PostAsJsonAsync("/cli/message", payload, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostMessage_MalformedJson_Returns400()
    {
        var content = new StringContent("not valid json", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/cli/message", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostMessage_OversizedPayload_Returns413()
    {
        // Generate payload > 100KB
        var largeContent = new string('x', 110_000);
        var payload = JsonSerializer.Serialize(new
        {
            type = "assistant.message",
            content = largeContent,
            messageId = "msg-big",
            timestamp = 1712345678000L
        });

        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/cli/message", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    // --- POST /cli/event (AC3) ---

    [Fact]
    public async Task PostEvent_ValidPayload_Returns200AndFiresEvent()
    {
        CliEvent? received = null;
        _server.SessionBridge.EventReceived += evt => received = evt;

        var payload = new
        {
            type = "session.start",
            data = new { sessionId = "s-1" },
            timestamp = 1712345678000L
        };

        var response = await _client.PostAsJsonAsync("/cli/event", payload, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(received);
        Assert.Equal("session.start", received!.Type);
    }

    [Fact]
    public async Task PostEvent_MissingType_Returns400()
    {
        var payload = new { timestamp = 123L }; // missing type

        var response = await _client.PostAsJsonAsync("/cli/event", payload, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- GET /cli/commands SSE (AC2, AC4) ---

    [Fact]
    public async Task SseCommands_SendsInitialPing()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var request = new HttpRequestMessage(HttpMethod.Get, "/cli/commands?sessionId=test-sse");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        // Read initial ping event
        var lines = await ReadSseEventAsync(reader, cts.Token);
        Assert.Contains(lines, l => l.StartsWith("event: ping"));
    }

    [Fact]
    public async Task SseCommands_DeliversQueuedCommand()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Start SSE connection
        var request = new HttpRequestMessage(HttpMethod.Get, "/cli/commands?sessionId=cmd-test");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        // Read and discard the initial ping
        await ReadSseEventAsync(reader, cts.Token);

        // Queue a command
        _server.SessionBridge.QueueCommand("cmd-test", new SendPromptCommand("Do something"));

        // Read the command event
        var lines = await ReadSseEventAsync(reader, cts.Token);
        var eventLine = lines.FirstOrDefault(l => l.StartsWith("event:"));
        var dataLine = lines.FirstOrDefault(l => l.StartsWith("data:"));

        Assert.NotNull(eventLine);
        Assert.Contains("send_prompt", eventLine!);
        Assert.NotNull(dataLine);
        Assert.Contains("Do something", dataLine!);
    }

    [Fact]
    public async Task SseCommands_GeneratesSessionIdIfNotProvided()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var request = new HttpRequestMessage(HttpMethod.Get, "/cli/commands"); // no sessionId param
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Server should have at least one session now
        // Read initial ping to ensure connection is established
        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        await ReadSseEventAsync(reader, cts.Token);

        Assert.NotEmpty(_server.SessionBridge.ConnectedSessions);
    }

    // --- POST /cli/send ---

    [Fact]
    public async Task PostSend_QueuesCommandForSession()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Start SSE to create the session
        var sseRequest = new HttpRequestMessage(HttpMethod.Get, "/cli/commands?sessionId=send-test");
        sseRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
        var sseResponse = await _client.SendAsync(sseRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var stream = await sseResponse.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        await ReadSseEventAsync(reader, cts.Token); // ping

        // POST /cli/send
        var payload = new { prompt = "Run tests", sessionId = "send-test" };
        var response = await _client.PostAsJsonAsync("/cli/send", payload, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify command delivered via SSE
        var lines = await ReadSseEventAsync(reader, cts.Token);
        Assert.Contains(lines, l => l.Contains("Run tests"));
    }

    // --- POST /speak ---

    [Fact]
    public async Task PostSpeak_ValidPayload_Returns200()
    {
        var payload = new { text = "Hello there" };
        var response = await _client.PostAsJsonAsync("/speak", payload, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostSpeak_MissingText_Returns400()
    {
        var payload = new { text = "" };
        var response = await _client.PostAsJsonAsync("/speak", payload, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- POST /avatar ---

    [Fact]
    public async Task PostAvatar_ValidPayload_Returns200()
    {
        var payload = new { expression = "thinking" };
        var response = await _client.PostAsJsonAsync("/avatar", payload, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostAvatar_MissingExpression_Returns400()
    {
        var payload = new { expression = "" };
        var response = await _client.PostAsJsonAsync("/avatar", payload, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- Multiple Sessions (AC5) ---

    [Fact]
    public async Task MultipleSessions_TrackedInHealthCount()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Connect two SSE sessions
        var req1 = new HttpRequestMessage(HttpMethod.Get, "/cli/commands?sessionId=s1");
        req1.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
        var resp1 = await _client.SendAsync(req1, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var stream1 = await resp1.Content.ReadAsStreamAsync(cts.Token);
        using var reader1 = new StreamReader(stream1);
        await ReadSseEventAsync(reader1, cts.Token);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "/cli/commands?sessionId=s2");
        req2.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
        var resp2 = await _client.SendAsync(req2, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var stream2 = await resp2.Content.ReadAsStreamAsync(cts.Token);
        using var reader2 = new StreamReader(stream2);
        await ReadSseEventAsync(reader2, cts.Token);

        // Check health
        var healthResp = await _client.GetAsync("/health");
        var body = await healthResp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("sessions").GetInt32() >= 2);
    }

    // --- Helpers ---

    /// <summary>Read one SSE event (lines until empty line).</summary>
    private static async Task<List<string>> ReadSseEventAsync(StreamReader reader, CancellationToken ct)
    {
        var lines = new List<string>();
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break; // stream closed
            if (line == string.Empty)
            {
                if (lines.Count > 0) break; // end of event
                continue; // skip leading empty lines
            }
            lines.Add(line);
        }
        return lines;
    }

    private static int GetRandomPort()
    {
        // Use port 0 trick to get an available port
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
