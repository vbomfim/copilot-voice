using System.Collections.Concurrent;
using System.Text.Json;

namespace CopilotVoice.Mcp;

/// <summary>
/// Represents a single MCP client connection. Each client has its own
/// input/output streams and pending sampling requests.
/// </summary>
public class McpClientConnection : IAsyncDisposable
{
    private readonly TextReader _reader;
    private readonly TextWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement?>> _pendingRequests = new();
    private int _nextRequestId;
    private CancellationTokenSource? _readCts;

    public string ClientId { get; }
    public bool IsInitialized { get; set; }
    public McpClientCapabilities? Capabilities { get; set; }

    public event Func<McpClientConnection, JsonRpcRequest, Task>? OnRequest;
    public event Action<McpClientConnection, JsonRpcNotification>? OnNotification;
    public event Action<McpClientConnection>? OnDisconnected;

    public McpClientConnection(string clientId, TextReader reader, TextWriter writer)
    {
        ClientId = clientId;
        _reader = reader;
        _writer = writer;
    }

    /// <summary>
    /// Start reading messages from this client's input stream.
    /// </summary>
    public Task StartReadingAsync(CancellationToken ct = default)
    {
        _readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        return ReadLoopAsync(_readCts.Token);
    }

    /// <summary>
    /// Send a JSON-RPC response to this client.
    /// </summary>
    public async Task SendResponseAsync(JsonRpcResponse response)
    {
        var json = JsonSerializer.Serialize(response, McpJsonOptions.Default);
        await WriteLineAsync(json);
    }

    /// <summary>
    /// Send a JSON-RPC notification to this client.
    /// </summary>
    public async Task SendNotificationAsync(string method, object? @params = null)
    {
        var notification = new JsonRpcNotification
        {
            Method = method,
            Params = @params,
        };
        var json = JsonSerializer.Serialize(notification, McpJsonOptions.Default);
        await WriteLineAsync(json);
    }

    /// <summary>
    /// Send a JSON-RPC request to this client and wait for the response.
    /// Used for sampling/createMessage where the server asks the client.
    /// </summary>
    public async Task<JsonElement?> SendRequestAsync(string method, object? @params, TimeSpan? timeout = null)
    {
        var id = $"srv-{Interlocked.Increment(ref _nextRequestId)}";
        var tcs = new TaskCompletionSource<JsonElement?>();
        _pendingRequests[id] = tcs;

        var request = new JsonRpcRequest
        {
            Id = id,
            Method = method,
            Params = @params != null
                ? JsonSerializer.SerializeToElement(@params, McpJsonOptions.Default)
                : null,
        };
        var json = JsonSerializer.Serialize(request, McpJsonOptions.Default);
        await WriteLineAsync(json);

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(effectiveTimeout);
        cts.Token.Register(() => tcs.TrySetCanceled());

        try
        {
            return await tcs.Task;
        }
        finally
        {
            _pendingRequests.TryRemove(id, out _);
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line == null) break; // EOF
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("id", out var idProp) && root.TryGetProperty("method", out _))
                    {
                        // Request (has both id and method)
                        var request = JsonSerializer.Deserialize<JsonRpcRequest>(line, McpJsonOptions.Default);
                        if (request != null && OnRequest != null)
                            await OnRequest(this, request);
                    }
                    else if (root.TryGetProperty("id", out _) && (root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _)))
                    {
                        // Response to a pending request we sent
                        var response = JsonSerializer.Deserialize<JsonRpcResponse>(line, McpJsonOptions.Default);
                        if (response?.Id != null)
                        {
                            var key = response.Id.ToString()!;
                            if (_pendingRequests.TryRemove(key, out var tcs))
                            {
                                if (response.Error != null)
                                    tcs.TrySetException(new McpException(response.Error));
                                else
                                    tcs.TrySetResult(response.Result != null
                                        ? JsonSerializer.SerializeToElement(response.Result, McpJsonOptions.Default)
                                        : null);
                            }
                        }
                    }
                    else if (root.TryGetProperty("method", out _))
                    {
                        // Notification (method but no id)
                        var notification = JsonSerializer.Deserialize<JsonRpcNotification>(line, McpJsonOptions.Default);
                        if (notification != null)
                            OnNotification?.Invoke(this, notification);
                    }
                }
                catch (JsonException)
                {
                    // Malformed JSON — skip
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally
        {
            OnDisconnected?.Invoke(this);
        }
    }

    private async Task WriteLineAsync(string json)
    {
        await _writeLock.WaitAsync();
        try
        {
            await _writer.WriteLineAsync(json);
            await _writer.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _readCts?.Cancel();
        _readCts?.Dispose();
        _writeLock.Dispose();
        foreach (var tcs in _pendingRequests.Values)
            tcs.TrySetCanceled();
        _pendingRequests.Clear();
        GC.SuppressFinalize(this);
    }
}

public class McpClientCapabilities
{
    public bool SupportsSampling { get; set; }
}

public class McpException : Exception
{
    public int Code { get; }
    public McpException(JsonRpcError error) : base(error.Message) => Code = error.Code;
}

public static class McpJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
