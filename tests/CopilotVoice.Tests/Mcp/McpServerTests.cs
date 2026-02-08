using System.Text.Json;
using CopilotVoice.Mcp;
using Xunit;

namespace CopilotVoice.Tests.Mcp;

public class McpServerTests
{
    /// <summary>Helper: create a pipe pair (client writes → server reads, server writes → client reads).</summary>
    private static (TextReader serverIn, TextWriter clientOut, TextReader clientIn, TextWriter serverOut) CreatePipe()
    {
        var clientToServer = new BlockingStream();
        var serverToClient = new BlockingStream();
        return (
            new StreamReader(clientToServer.ReadStream),
            new StreamWriter(clientToServer.WriteStream) { AutoFlush = true },
            new StreamReader(serverToClient.ReadStream),
            new StreamWriter(serverToClient.WriteStream) { AutoFlush = true }
        );
    }

    [Fact]
    public async Task Initialize_ReturnsServerInfo()
    {
        await using var server = new McpServer();
        var (serverIn, clientOut, clientIn, serverOut) = CreatePipe();

        var client = await server.AddClientAsync(serverIn, serverOut);

        // Client sends initialize request
        var initRequest = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { sampling = new { } },
                clientInfo = new { name = "test-client", version = "1.0" },
            }
        });
        await clientOut.WriteLineAsync(initRequest);

        // Read response
        var responseLine = await ReadLineWithTimeoutAsync(clientIn);
        Assert.NotNull(responseLine);

        var response = JsonSerializer.Deserialize<JsonRpcResponse>(responseLine!, McpJsonOptions.Default);
        Assert.NotNull(response);
        Assert.Equal(1, ((JsonElement)response!.Id!).GetInt32());
        Assert.Null(response.Error);

        var result = (JsonElement)response.Result!;
        Assert.Equal("copilot-voice", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Equal("2024-11-05", result.GetProperty("protocolVersion").GetString());

        // Should also receive initialized notification
        var notifLine = await ReadLineWithTimeoutAsync(clientIn);
        Assert.NotNull(notifLine);
        Assert.Contains("notifications/initialized", notifLine!);
    }

    [Fact]
    public async Task Initialize_DetectsSamplingCapability()
    {
        await using var server = new McpServer();
        var (serverIn, clientOut, clientIn, serverOut) = CreatePipe();

        var client = await server.AddClientAsync(serverIn, serverOut);

        // Initialize WITH sampling capability
        await SendInitializeAsync(clientOut, withSampling: true);
        await ReadLineWithTimeoutAsync(clientIn); // response
        await ReadLineWithTimeoutAsync(clientIn); // initialized notification

        Assert.True(client.IsInitialized);
        Assert.True(client.Capabilities?.SupportsSampling);
    }

    [Fact]
    public async Task Initialize_NoSamplingCapability()
    {
        await using var server = new McpServer();
        var (serverIn, clientOut, clientIn, serverOut) = CreatePipe();

        var client = await server.AddClientAsync(serverIn, serverOut);

        // Initialize WITHOUT sampling capability
        await SendInitializeAsync(clientOut, withSampling: false);
        await ReadLineWithTimeoutAsync(clientIn); // response
        await ReadLineWithTimeoutAsync(clientIn); // initialized notification

        Assert.True(client.IsInitialized);
        Assert.False(client.Capabilities?.SupportsSampling);
    }

    [Fact]
    public async Task ToolsList_ReturnsAllTools()
    {
        await using var server = new McpServer();
        var (serverIn, clientOut, clientIn, serverOut) = CreatePipe();

        await server.AddClientAsync(serverIn, serverOut);
        await SendInitializeAsync(clientOut, withSampling: false);
        await ReadLineWithTimeoutAsync(clientIn); // init response
        await ReadLineWithTimeoutAsync(clientIn); // initialized notification

        // Request tools list
        var toolsRequest = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/list",
        });
        await clientOut.WriteLineAsync(toolsRequest);

        var responseLine = await ReadLineWithTimeoutAsync(clientIn);
        Assert.NotNull(responseLine);

        var doc = JsonDocument.Parse(responseLine!);
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        var toolNames = tools.EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToList();

        Assert.Contains("speak", toolNames);
        Assert.Contains("listen", toolNames);
        Assert.Contains("set_avatar", toolNames);
        Assert.Contains("notify", toolNames);
        Assert.Equal(4, toolNames.Count);
    }

    [Fact]
    public async Task ToolsCall_Speak_InvokesHandler()
    {
        await using var server = new McpServer();
        var (serverIn, clientOut, clientIn, serverOut) = CreatePipe();

        string? spokenText = null;
        McpToolHandler.OnSpeak = (text, voice) =>
        {
            spokenText = text;
            return Task.CompletedTask;
        };

        await server.AddClientAsync(serverIn, serverOut);
        await SendInitializeAsync(clientOut, withSampling: false);
        await ReadLineWithTimeoutAsync(clientIn); // init response
        await ReadLineWithTimeoutAsync(clientIn); // initialized notification

        var callRequest = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new
            {
                name = "speak",
                arguments = new { text = "Hello world" },
            }
        });
        await clientOut.WriteLineAsync(callRequest);

        var responseLine = await ReadLineWithTimeoutAsync(clientIn);
        Assert.NotNull(responseLine);
        Assert.DoesNotContain("error", responseLine!);

        Assert.Equal("Hello world", spokenText);

        McpToolHandler.OnSpeak = null; // cleanup
    }

    [Fact]
    public async Task ToolsCall_SetAvatar_InvokesHandler()
    {
        await using var server = new McpServer();
        var (serverIn, clientOut, clientIn, serverOut) = CreatePipe();

        string? setExpression = null;
        McpToolHandler.OnSetAvatar = expr => setExpression = expr;

        await server.AddClientAsync(serverIn, serverOut);
        await SendInitializeAsync(clientOut, withSampling: false);
        await ReadLineWithTimeoutAsync(clientIn);
        await ReadLineWithTimeoutAsync(clientIn);

        var callRequest = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 4,
            method = "tools/call",
            @params = new
            {
                name = "set_avatar",
                arguments = new { expression = "thinking" },
            }
        });
        await clientOut.WriteLineAsync(callRequest);

        var responseLine = await ReadLineWithTimeoutAsync(clientIn);
        Assert.NotNull(responseLine);
        Assert.Equal("thinking", setExpression);

        McpToolHandler.OnSetAvatar = null;
    }

    [Fact]
    public async Task Sampling_SendsCreateMessageToClient()
    {
        await using var server = new McpServer();
        var (serverIn, clientOut, clientIn, serverOut) = CreatePipe();

        var client = await server.AddClientAsync(serverIn, serverOut);
        await SendInitializeAsync(clientOut, withSampling: true);
        await ReadLineWithTimeoutAsync(clientIn); // init response
        await ReadLineWithTimeoutAsync(clientIn); // initialized notification

        // Start sampling request (server → client) in background
        var samplingTask = Task.Run(() =>
            server.RequestSamplingAsync(client, "fix the login bug", TimeSpan.FromSeconds(5)));

        // Client should receive the sampling request
        var requestLine = await ReadLineWithTimeoutAsync(clientIn);
        Assert.NotNull(requestLine);
        Assert.Contains("sampling/createMessage", requestLine!);

        var doc = JsonDocument.Parse(requestLine!);
        var id = doc.RootElement.GetProperty("id").GetString();
        var messages = doc.RootElement.GetProperty("params").GetProperty("messages");
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("fix the login bug", messages[0].GetProperty("content").GetProperty("text").GetString());

        // Client sends response back
        var samplingResponse = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            result = new
            {
                role = "assistant",
                content = new { type = "text", text = "I'll fix the login bug now." },
                model = "claude-sonnet-4",
                stopReason = "endTurn",
            }
        });
        await clientOut.WriteLineAsync(samplingResponse);

        var result = await samplingTask;
        Assert.NotNull(result);
        Assert.Equal("assistant", result!.Role);
        Assert.Equal("I'll fix the login bug now.", result.Content?.Text);
    }

    [Fact]
    public async Task MultipleClients_IndependentSessions()
    {
        await using var server = new McpServer();

        // Client 1
        var (s1In, c1Out, c1In, s1Out) = CreatePipe();
        var client1 = await server.AddClientAsync(s1In, s1Out);
        await SendInitializeAsync(c1Out, withSampling: true);
        await ReadLineWithTimeoutAsync(c1In);
        await ReadLineWithTimeoutAsync(c1In);

        // Client 2
        var (s2In, c2Out, c2In, s2Out) = CreatePipe();
        var client2 = await server.AddClientAsync(s2In, s2Out);
        await SendInitializeAsync(c2Out, withSampling: false);
        await ReadLineWithTimeoutAsync(c2In);
        await ReadLineWithTimeoutAsync(c2In);

        Assert.Equal(2, server.Clients.Count);
        Assert.True(client1.Capabilities?.SupportsSampling);
        Assert.False(client2.Capabilities?.SupportsSampling);
    }

    [Fact]
    public async Task BroadcastSampling_OnlySendsToSamplingClients()
    {
        await using var server = new McpServer();

        // Client 1 — supports sampling
        var (s1In, c1Out, c1In, s1Out) = CreatePipe();
        var client1 = await server.AddClientAsync(s1In, s1Out);
        await SendInitializeAsync(c1Out, withSampling: true);
        await ReadLineWithTimeoutAsync(c1In);
        await ReadLineWithTimeoutAsync(c1In);

        // Client 2 — no sampling
        var (s2In, c2Out, c2In, s2Out) = CreatePipe();
        await server.AddClientAsync(s2In, s2Out);
        await SendInitializeAsync(c2Out, withSampling: false);
        await ReadLineWithTimeoutAsync(c2In);
        await ReadLineWithTimeoutAsync(c2In);

        // Broadcast sampling
        var broadcastTask = Task.Run(() =>
            server.BroadcastSamplingAsync("hello from voice", TimeSpan.FromSeconds(3)));

        // Only client1 should receive it
        var req1 = await ReadLineWithTimeoutAsync(c1In);
        Assert.NotNull(req1);
        Assert.Contains("sampling/createMessage", req1!);

        // Client2 should NOT receive anything (with short timeout)
        var req2 = await ReadLineWithTimeoutAsync(c2In, timeout: TimeSpan.FromMilliseconds(200));
        Assert.Null(req2);

        // Respond from client1
        var doc = JsonDocument.Parse(req1!);
        var id = doc.RootElement.GetProperty("id").GetString();
        await c1Out.WriteLineAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            result = new
            {
                role = "assistant",
                content = new { type = "text", text = "Got it" },
                model = "test",
                stopReason = "endTurn",
            }
        }));

        var result = await broadcastTask;
        Assert.NotNull(result);
        Assert.Equal("Got it", result!.Content?.Text);
    }

    [Fact]
    public async Task UnknownMethod_ReturnsMethodNotFound()
    {
        await using var server = new McpServer();
        var (serverIn, clientOut, clientIn, serverOut) = CreatePipe();

        await server.AddClientAsync(serverIn, serverOut);

        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 99,
            method = "nonexistent/method",
        });
        await clientOut.WriteLineAsync(request);

        var responseLine = await ReadLineWithTimeoutAsync(clientIn);
        Assert.NotNull(responseLine);

        var doc = JsonDocument.Parse(responseLine!);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal(-32601, error.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Ping_ReturnsPong()
    {
        await using var server = new McpServer();
        var (serverIn, clientOut, clientIn, serverOut) = CreatePipe();

        await server.AddClientAsync(serverIn, serverOut);

        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 10,
            method = "ping",
        });
        await clientOut.WriteLineAsync(request);

        var responseLine = await ReadLineWithTimeoutAsync(clientIn);
        Assert.NotNull(responseLine);

        var doc = JsonDocument.Parse(responseLine!);
        Assert.True(doc.RootElement.TryGetProperty("result", out _));
        Assert.False(doc.RootElement.TryGetProperty("error", out _));
    }

    // --- Helpers ---

    private static async Task SendInitializeAsync(TextWriter clientOut, bool withSampling)
    {
        object capabilities = withSampling
            ? new { sampling = new { } }
            : new { };

        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities,
                clientInfo = new { name = "test-client", version = "1.0" },
            }
        });
        await clientOut.WriteLineAsync(request);
    }

    private static async Task<string?> ReadLineWithTimeoutAsync(
        TextReader reader, TimeSpan? timeout = null)
    {
        var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));
        try
        {
            return await reader.ReadLineAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}

/// <summary>
/// A simple in-memory stream pair for testing: one side writes, the other reads.
/// </summary>
internal class BlockingStream
{
    private readonly System.IO.Pipelines.Pipe _pipe = new();

    public Stream ReadStream => _pipe.Reader.AsStream();
    public Stream WriteStream => _pipe.Writer.AsStream();
}
