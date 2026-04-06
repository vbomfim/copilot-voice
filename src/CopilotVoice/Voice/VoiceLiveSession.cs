using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CopilotVoice.Voice;

/// <summary>
/// Active Voice Live API session. Wraps an IRealtimeConnection with a background
/// receive loop that dispatches events via Action delegates.
/// Handles automatic reconnection with exponential backoff on WebSocket drops.
/// </summary>
public sealed class VoiceLiveSession : IVoiceLiveSession
{
    private readonly VoiceLiveConfig _config;
    private readonly Func<IRealtimeConnection> _connectionFactory;
    private IRealtimeConnection _connection;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private bool _disposed;

    private const int MaxReconnectAttempts = 10;
    private const double MaxBackoffSeconds = 30.0;

    // Events
    public event Action<ReadOnlyMemory<byte>>? AudioReceived;
    public event Action<string>? TranscriptReceived;
    public event Action<FunctionCall>? FunctionCallReceived;
    public event Action<string>? ErrorReceived;
    public event Action? SessionReady;
    public event Action? ResponseDone;
    public event Action? Disconnected;

    /// <summary>Exposed for testing — allows tests to observe reconnection timing.</summary>
    internal event Action<int, TimeSpan>? ReconnectAttempted;

    /// <summary>Delay function — defaults to Task.Delay, overridden in tests for speed.</summary>
    internal Func<TimeSpan, CancellationToken, Task> DelayFunc { get; set; } = Task.Delay;

    internal VoiceLiveSession(
        VoiceLiveConfig config,
        IRealtimeConnection connection,
        Func<IRealtimeConnection> connectionFactory)
    {
        _config = config;
        _connection = connection;
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Connect to the API and start the background receive loop.
    /// Sends the initial session.update configuration event.
    /// </summary>
    internal async Task StartAsync(CancellationToken ct)
    {
        var uri = BuildWebSocketUri(_config);
        var headers = BuildHeaders(_config);

        await _connection.ConnectAsync(uri, headers, ct).ConfigureAwait(false);
        await SendInitialSessionUpdateAsync(ct).ConfigureAwait(false);

        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _receiveTask = Task.Run(() => ReceiveLoopWithReconnectionAsync(_receiveCts.Token));
    }

    public async Task SendAudioAsync(ReadOnlyMemory<byte> pcm16Audio, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var json = JsonSerializer.Serialize(new
        {
            type = "input_audio_buffer.append",
            audio = Convert.ToBase64String(pcm16Audio.Span)
        });

        await _connection.SendEventAsync(json, ct).ConfigureAwait(false);
    }

    public async Task CommitAudioAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var json = JsonSerializer.Serialize(new { type = "input_audio_buffer.commit" });
        await _connection.SendEventAsync(json, ct).ConfigureAwait(false);
    }

    public async Task SendTextAsync(string text, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var json = JsonSerializer.Serialize(new
        {
            type = "conversation.item.create",
            item = new
            {
                type = "message",
                role = "user",
                content = new object[]
                {
                    new { type = "input_text", text }
                }
            }
        });

        await _connection.SendEventAsync(json, ct).ConfigureAwait(false);

        // Also trigger a response
        var responseJson = JsonSerializer.Serialize(new { type = "response.create" });
        await _connection.SendEventAsync(responseJson, ct).ConfigureAwait(false);
    }

    public async Task UpdateSessionAsync(SessionUpdate update, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var json = SerializeSessionUpdate(update);
        await _connection.SendEventAsync(json, ct).ConfigureAwait(false);
    }

    public async Task SendFunctionResultAsync(string callId, string result, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var json = JsonSerializer.Serialize(new
        {
            type = "conversation.item.create",
            item = new
            {
                type = "function_call_output",
                call_id = callId,
                output = result
            }
        });

        await _connection.SendEventAsync(json, ct).ConfigureAwait(false);

        // Trigger the model to continue responding after function result
        var responseJson = JsonSerializer.Serialize(new { type = "response.create" });
        await _connection.SendEventAsync(responseJson, ct).ConfigureAwait(false);
    }

    // --- Receive loop with reconnection ---

    private async Task ReceiveLoopWithReconnectionAsync(CancellationToken ct)
    {
        while (!_disposed && !ct.IsCancellationRequested)
        {
            try
            {
                await foreach (var json in _connection.ReceiveEventsAsync(ct).ConfigureAwait(false))
                {
                    DispatchEvent(json);
                }

                // Enumerable completed — connection closed
                if (_disposed || ct.IsCancellationRequested)
                    break;

                // Try to reconnect
                if (!await TryReconnectAsync(ct).ConfigureAwait(false))
                    break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (IsAuthFailure(ex))
                {
                    ErrorReceived?.Invoke($"Authentication failed: {ex.Message}");
                    break;
                }

                // Unexpected error — attempt reconnection
                if (!await TryReconnectAsync(ct).ConfigureAwait(false))
                    break;
            }
        }

        if (!_disposed)
            Disconnected?.Invoke();
    }

    private async Task<bool> TryReconnectAsync(CancellationToken ct)
    {
        for (int attempt = 0; attempt < MaxReconnectAttempts; attempt++)
        {
            if (_disposed || ct.IsCancellationRequested)
                return false;

            var delaySeconds = Math.Min(Math.Pow(2, attempt), MaxBackoffSeconds);
            var delay = TimeSpan.FromSeconds(delaySeconds);

            ReconnectAttempted?.Invoke(attempt, delay);

            try
            {
                await DelayFunc(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            try
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = _connectionFactory();

                var uri = BuildWebSocketUri(_config);
                var headers = BuildHeaders(_config);
                await _connection.ConnectAsync(uri, headers, ct).ConfigureAwait(false);
                await SendInitialSessionUpdateAsync(ct).ConfigureAwait(false);

                return true; // Reconnected successfully
            }
            catch (Exception ex) when (IsAuthFailure(ex))
            {
                ErrorReceived?.Invoke($"Authentication failed during reconnection: {ex.Message}");
                return false;
            }
            catch
            {
                // Retry next iteration
            }
        }

        ErrorReceived?.Invoke($"Failed to reconnect after {MaxReconnectAttempts} attempts.");
        return false;
    }

    // --- Event dispatch ---

    internal void DispatchEvent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
                return;

            var type = typeProp.GetString();
            Console.Error.WriteLine($"[VoiceLive] Event: {type}");

            switch (type)
            {
                case "session.created":
                case "session.updated":
                    SessionReady?.Invoke();
                    break;

                case "response.audio.delta":
                    if (root.TryGetProperty("delta", out var audioDelta))
                    {
                        var base64 = audioDelta.GetString();
                        if (base64 is not null)
                            AudioReceived?.Invoke(Convert.FromBase64String(base64));
                    }
                    break;

                case "response.audio_transcript.delta":
                    if (root.TryGetProperty("delta", out var transcriptDelta))
                    {
                        var text = transcriptDelta.GetString();
                        if (text is not null)
                            TranscriptReceived?.Invoke(text);
                    }
                    break;

                case "response.function_call_arguments.done":
                    {
                        var callId = root.GetProperty("call_id").GetString()!;
                        var name = root.GetProperty("name").GetString()!;
                        var args = root.GetProperty("arguments").GetString()!;
                        FunctionCallReceived?.Invoke(new FunctionCall(callId, name, args));
                    }
                    break;

                case "error":
                    Console.Error.WriteLine($"[VoiceLive] Error event: {json[..Math.Min(json.Length, 500)]}");
                    if (root.TryGetProperty("error", out var errorObj) &&
                        errorObj.TryGetProperty("message", out var errorMsg))
                    {
                        ErrorReceived?.Invoke(errorMsg.GetString() ?? "Unknown error");
                    }
                    else
                    {
                        ErrorReceived?.Invoke("Unknown error");
                    }
                    break;

                case "response.done":
                    ResponseDone?.Invoke();
                    break;

                // Silently ignore other event types (input_audio_buffer.speech_started, etc.)
            }
        }
        catch (JsonException)
        {
            ErrorReceived?.Invoke("Failed to parse server event.");
        }
    }

    // --- Helpers ---

    private async Task SendInitialSessionUpdateAsync(CancellationToken ct)
    {
        var tools = FunctionCallHandler.GetToolDefinitions();
        var update = new SessionUpdate(
            Modalities: new[] { "audio", "text" },
            Voice: _config.Voice,
            Instructions: string.IsNullOrEmpty(_config.SystemInstructions)
                ? "You are a voice interface for GitHub Copilot CLI. Help the developer by executing commands, reading files, and providing context about their coding session."
                : _config.SystemInstructions,
            Tools: tools
        );

        var json = SerializeSessionUpdate(update);
        await _connection.SendEventAsync(json, ct).ConfigureAwait(false);
    }

    internal static string SerializeSessionUpdate(SessionUpdate update)
    {
        var tools = update.Tools?.Select(t => new
        {
            type = "function",
            name = t.Name,
            description = t.Description,
            parameters = JsonSerializer.Deserialize<JsonElement>(t.ParametersJson)
        }).ToArray();

        return JsonSerializer.Serialize(new
        {
            type = "session.update",
            session = new
            {
                modalities = update.Modalities,
                voice = update.Voice,
                instructions = update.Instructions,
                input_audio_format = update.InputAudioFormat,
                output_audio_format = update.OutputAudioFormat,
                tools,
                tool_choice = update.ToolChoice
            }
        });
    }

    internal static Uri BuildWebSocketUri(VoiceLiveConfig config)
    {
        var endpoint = config.Endpoint.TrimEnd('/');

        // If the endpoint already contains the full realtime URL path, use it directly
        if (endpoint.Contains("/openai/realtime", StringComparison.OrdinalIgnoreCase))
        {
            if (endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                endpoint = "wss://" + endpoint[8..];
            return new Uri(endpoint);
        }

        // Azure Realtime API requires the openai.azure.com domain, not cognitiveservices.azure.com
        endpoint = endpoint.Replace(".cognitiveservices.azure.com", ".openai.azure.com",
            StringComparison.OrdinalIgnoreCase);

        // Strip https:// and replace with wss://
        if (endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            endpoint = "wss://" + endpoint[8..];
        else if (!endpoint.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            endpoint = "wss://" + endpoint;

        return new Uri(
            $"{endpoint}/openai/realtime?api-version=2024-10-01-preview&deployment={config.Model}");
    }

    internal static IDictionary<string, string> BuildHeaders(VoiceLiveConfig config)
    {
        var headers = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(config.ApiKey))
            headers["api-key"] = config.ApiKey;

        return headers;
    }

    private static bool IsAuthFailure(Exception ex)
    {
        // WebSocket handshake failures for 401/403 surface as WebSocketException
        if (ex is WebSocketException wsEx)
        {
            var message = wsEx.Message;
            return message.Contains("401") || message.Contains("403") ||
                   message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        _receiveCts?.Cancel();

        try
        {
            if (_receiveTask is not null)
                await _receiveTask.ConfigureAwait(false);
        }
        catch
        {
            // Swallow — the receive loop handles its own errors
        }

        await _connection.DisposeAsync().ConfigureAwait(false);
        _receiveCts?.Dispose();
    }
}
