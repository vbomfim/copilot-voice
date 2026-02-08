using System.Collections.Concurrent;
using System.Text.Json;

namespace CopilotVoice.Mcp;

/// <summary>
/// MCP server that handles multiple client connections.
/// Each client connects via its own stdio streams (or HTTP in the future).
/// Provides tool handling and sampling (server→client prompt push).
/// </summary>
public class McpServer : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, McpClientConnection> _clients = new();
    private int _clientCounter;

    public const string ServerName = "copilot-voice";
    public const string ServerVersion = "1.0.0";

    public event Action<string>? OnLog;

    /// <summary>All currently connected clients.</summary>
    public IReadOnlyCollection<McpClientConnection> Clients => _clients.Values.ToList();

    /// <summary>
    /// Register a new client connection and start processing its messages.
    /// </summary>
    public async Task<McpClientConnection> AddClientAsync(TextReader reader, TextWriter writer, CancellationToken ct = default)
    {
        var id = $"client-{Interlocked.Increment(ref _clientCounter)}";
        var connection = new McpClientConnection(id, reader, writer);

        connection.OnRequest += HandleRequestAsync;
        connection.OnNotification += HandleNotification;
        connection.OnDisconnected += HandleDisconnected;

        _clients[id] = connection;
        Log($"Client {id} connected ({_clients.Count} total)");

        _ = connection.StartReadingAsync(ct);
        return connection;
    }

    /// <summary>
    /// Send a sampling/createMessage request to a specific client.
    /// This pushes a voice transcription as a prompt to the CLI.
    /// </summary>
    public async Task<SamplingResult?> RequestSamplingAsync(
        McpClientConnection client, string userMessage, TimeSpan? timeout = null)
    {
        if (!client.IsInitialized)
            throw new InvalidOperationException("Client not initialized");
        if (client.Capabilities?.SupportsSampling != true)
            throw new InvalidOperationException("Client does not support sampling");

        var @params = new
        {
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new { type = "text", text = userMessage }
                }
            },
            maxTokens = 4096,
            includeContext = "allServers",
        };

        Log($"Sending sampling/createMessage to {client.ClientId}: \"{Truncate(userMessage, 60)}\"");
        var result = await client.SendRequestAsync("sampling/createMessage", @params, timeout);

        if (result == null) return null;

        return JsonSerializer.Deserialize<SamplingResult>(result.Value, McpJsonOptions.Default);
    }

    /// <summary>
    /// Broadcast a sampling/createMessage to ALL connected clients that support sampling.
    /// Returns the first successful response.
    /// </summary>
    public async Task<SamplingResult?> BroadcastSamplingAsync(string userMessage, TimeSpan? timeout = null)
    {
        var samplingClients = _clients.Values
            .Where(c => c.IsInitialized && c.Capabilities?.SupportsSampling == true)
            .ToList();

        if (samplingClients.Count == 0)
        {
            Log("No clients support sampling");
            return null;
        }

        // Send to all, return first response
        var tasks = samplingClients.Select(c =>
            RequestSamplingAsync(c, userMessage, timeout));

        try
        {
            var completed = await Task.WhenAny(tasks);
            return await completed;
        }
        catch (Exception ex)
        {
            Log($"Broadcast sampling failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Send a notification to all connected clients.
    /// </summary>
    public async Task BroadcastNotificationAsync(string method, object? @params = null)
    {
        foreach (var client in _clients.Values)
        {
            try
            {
                await client.SendNotificationAsync(method, @params);
            }
            catch { /* client may have disconnected */ }
        }
    }

    private async Task HandleRequestAsync(McpClientConnection client, JsonRpcRequest request)
    {
        Log($"[{client.ClientId}] Request: {request.Method}");

        var response = request.Method switch
        {
            "initialize" => HandleInitialize(client, request),
            "tools/list" => HandleToolsList(request),
            "tools/call" => await HandleToolsCallAsync(request),
            "ping" => new JsonRpcResponse { Id = request.Id, Result = new { } },
            _ => new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError
                {
                    Code = JsonRpcError.MethodNotFound,
                    Message = $"Unknown method: {request.Method}"
                }
            },
        };

        await client.SendResponseAsync(response);

        // After initialize response, send initialized notification
        if (request.Method == "initialize")
            await client.SendNotificationAsync("notifications/initialized");
    }

    private JsonRpcResponse HandleInitialize(McpClientConnection client, JsonRpcRequest request)
    {
        // Parse client capabilities
        if (request.Params.HasValue)
        {
            var root = request.Params.Value;
            if (root.TryGetProperty("capabilities", out var caps))
            {
                client.Capabilities = new McpClientCapabilities
                {
                    SupportsSampling = caps.TryGetProperty("sampling", out _),
                };
            }
        }
        client.Capabilities ??= new McpClientCapabilities();
        client.IsInitialized = true;

        Log($"[{client.ClientId}] Initialized (sampling={client.Capabilities.SupportsSampling})");

        return new JsonRpcResponse
        {
            Id = request.Id,
            Result = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new
                {
                    tools = new { },
                    // We declare that we may send sampling requests
                },
                serverInfo = new
                {
                    name = ServerName,
                    version = ServerVersion,
                },
            },
        };
    }

    private JsonRpcResponse HandleToolsList(JsonRpcRequest request)
    {
        return new JsonRpcResponse
        {
            Id = request.Id,
            Result = new
            {
                tools = McpToolDefinitions.All,
            },
        };
    }

    private async Task<JsonRpcResponse> HandleToolsCallAsync(JsonRpcRequest request)
    {
        if (!request.Params.HasValue)
            return ErrorResponse(request.Id, JsonRpcError.InvalidParams, "Missing params");

        var root = request.Params.Value;
        if (!root.TryGetProperty("name", out var nameProp))
            return ErrorResponse(request.Id, JsonRpcError.InvalidParams, "Missing tool name");

        var toolName = nameProp.GetString() ?? "";
        var args = root.TryGetProperty("arguments", out var argsProp)
            ? argsProp
            : (JsonElement?)null;

        Log($"Tool call: {toolName}");

        try
        {
            var result = await McpToolHandler.HandleAsync(toolName, args);
            return new JsonRpcResponse
            {
                Id = request.Id,
                Result = result,
            };
        }
        catch (Exception ex)
        {
            return ErrorResponse(request.Id, JsonRpcError.InternalError, ex.Message);
        }
    }

    private void HandleNotification(McpClientConnection client, JsonRpcNotification notification)
    {
        Log($"[{client.ClientId}] Notification: {notification.Method}");

        if (notification.Method == "notifications/initialized")
            Log($"[{client.ClientId}] Client confirmed initialized");
    }

    private void HandleDisconnected(McpClientConnection client)
    {
        _clients.TryRemove(client.ClientId, out _);
        Log($"Client {client.ClientId} disconnected ({_clients.Count} remaining)");
    }

    private static JsonRpcResponse ErrorResponse(object? id, int code, string message) =>
        new() { Id = id, Error = new JsonRpcError { Code = code, Message = message } };

    private void Log(string msg) => OnLog?.Invoke($"[MCP] {msg}");

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "...";

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients.Values)
            await client.DisposeAsync();
        _clients.Clear();
        GC.SuppressFinalize(this);
    }
}

public class SamplingResult
{
    public string? Role { get; set; }
    public SamplingContent? Content { get; set; }
    public string? Model { get; set; }
    public string? StopReason { get; set; }
}

public class SamplingContent
{
    public string? Type { get; set; }
    public string? Text { get; set; }
}
