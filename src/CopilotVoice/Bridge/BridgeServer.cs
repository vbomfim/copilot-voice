using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace CopilotVoice.Bridge;

/// <summary>
/// HTTP bridge server using ASP.NET Core minimal APIs.
/// Binds to 127.0.0.1 only for security. Provides endpoints for
/// the CLI extension to communicate with the companion app.
/// </summary>
public sealed class BridgeServer : IAsyncDisposable
{
    private readonly int _port;
    private WebApplication? _app;
    private Task? _runTask;

    /// <summary>The session bridge used for message routing and command queuing.</summary>
    public ISessionBridge SessionBridge => _sessionBridge;

    private readonly SessionBridge _sessionBridge = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Maximum allowed request body size in bytes (100KB).</summary>
    private const int MaxBodySize = 100 * 1024;

    /// <summary>Maximum concurrent SSE sessions.</summary>
    internal const int MaxSessions = 10;

    /// <summary>Fired when a speak request is received (text to be spoken).</summary>
    public event Action<string>? SpeakRequested;

    /// <summary>Last transcript received from the Realtime API in response to a /speak request.</summary>
    public string? LastSpeakTranscript { get; private set; }

    /// <summary>Fired when the Realtime API finishes responding to a /speak request.</summary>
    public event Action<string>? SpeakResponseReady;

    /// <summary>Raise the SpeakResponseReady event from outside the class.</summary>
    public void NotifySpeakResponse(string transcript) => SpeakResponseReady?.Invoke(transcript);

    /// <summary>Fired when a Talk Mode toggle is requested.</summary>
    public event Action? TalkModeToggleRequested;

    /// <summary>Fired when an avatar expression change is requested.</summary>
    public event Action<string>? AvatarRequested;

    public BridgeServer(int port = 7701)
    {
        _port = port;
    }

    /// <summary>Start the HTTP server, binding to localhost only.</summary>
    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{_port}");
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = MaxBodySize);

        // Suppress noisy request logging in tests
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        _app = builder.Build();
        MapEndpoints(_app);

        _runTask = _app.StartAsync();
        await _runTask;
    }

    /// <summary>Stop the HTTP server gracefully.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet("/health", () => HandleHealth());
        app.MapPost("/cli/message", (Delegate)HandleCliMessage);
        app.MapPost("/cli/event", (Delegate)HandleCliEvent);
        app.MapGet("/cli/commands", (Delegate)(async (HttpContext ctx) => await HandleCliCommands(ctx)));
        app.MapPost("/cli/send", (Delegate)HandleCliSend);
        app.MapPost("/speak", (Delegate)HandleSpeak);
        app.MapPost("/avatar", (Delegate)HandleAvatar);
        app.MapPost("/talkmode", (Delegate)HandleTalkMode);
    }

    private IResult HandleHealth()
    {
        return Results.Ok(new
        {
            status = "ok",
            sessions = _sessionBridge.ConnectedSessions.Count
        });
    }

    private async Task<IResult> HandleCliMessage(HttpContext context)
    {
        var body = await ReadBodyWithSizeLimitAsync(context);
        if (body is null)
            return Results.StatusCode(413);

        CliMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<CliMessage>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "malformed JSON" });
        }

        if (message is null
            || string.IsNullOrWhiteSpace(message.Type)
            || string.IsNullOrWhiteSpace(message.Content)
            || string.IsNullOrWhiteSpace(message.MessageId)
            || message.Timestamp == 0)
        {
            return Results.BadRequest(new { error = "missing required fields: type, content, messageId, timestamp" });
        }

        _sessionBridge.OnMessageReceived(message);
        return Results.Ok(new { status = "ok" });
    }

    private async Task<IResult> HandleCliEvent(HttpContext context)
    {
        var body = await ReadBodyWithSizeLimitAsync(context);
        if (body is null)
            return Results.StatusCode(413);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "malformed JSON" });
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("type", out var typeProp)
                || typeProp.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(typeProp.GetString()))
            {
                return Results.BadRequest(new { error = "missing required field: type" });
            }

            if (!doc.RootElement.TryGetProperty("timestamp", out var tsProp)
                || tsProp.ValueKind != JsonValueKind.Number)
            {
                return Results.BadRequest(new { error = "missing required field: timestamp" });
            }

            object? data = null;
            if (doc.RootElement.TryGetProperty("data", out var dataProp))
            {
                data = dataProp.Clone();
            }

            var evt = new CliEvent(
                typeProp.GetString()!,
                data,
                tsProp.GetInt64());

            _sessionBridge.OnEventReceived(evt);
        }

        return Results.Ok(new { status = "ok" });
    }

    private async Task HandleCliCommands(HttpContext context)
    {
        var sessionId = context.Request.Query["sessionId"].FirstOrDefault()
                        ?? Guid.NewGuid().ToString("N");

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        // Send initial ping
        await WriteSseEventAsync(context.Response, "ping", "{}");

        // Stream commands until client disconnects
        try
        {
            await foreach (var command in _sessionBridge.GetCommandStream(
                               sessionId, context.RequestAborted))
            {
                var data = JsonSerializer.Serialize(command, JsonOptions);
                await WriteSseEventAsync(context.Response, "send_prompt", data);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — clean up after delay
        }
        finally
        {
            // Schedule session removal after 30s timeout (allow reconnection)
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30));
                    _sessionBridge.RemoveSession(sessionId);
                }
                catch (Exception)
                {
                    // Cleanup is best-effort — don't propagate
                }
            });
        }
    }

    private async Task<IResult> HandleCliSend(HttpContext context)
    {
        var body = await ReadBodyWithSizeLimitAsync(context);
        if (body is null)
            return Results.StatusCode(413);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "malformed JSON" });
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("prompt", out var promptProp)
                || promptProp.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(promptProp.GetString()))
            {
                return Results.BadRequest(new { error = "missing required field: prompt" });
            }

            var prompt = promptProp.GetString()!;
            var command = new SendPromptCommand(prompt);

            if (doc.RootElement.TryGetProperty("sessionId", out var sidProp)
                && sidProp.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(sidProp.GetString()))
            {
                _sessionBridge.QueueCommand(sidProp.GetString()!, command);
            }
            else
            {
                // Broadcast to all sessions
                foreach (var session in _sessionBridge.ConnectedSessions)
                {
                    _sessionBridge.QueueCommand(session, command);
                }
            }
        }

        return Results.Ok(new { status = "ok" });
    }

    private async Task<IResult> HandleSpeak(HttpContext context)
    {
        var body = await ReadBodyWithSizeLimitAsync(context);
        if (body is null)
            return Results.StatusCode(413);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "malformed JSON" });
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("text", out var textProp)
                || textProp.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(textProp.GetString()))
            {
                return Results.BadRequest(new { error = "missing required field: text" });
            }

            // Wait for the Realtime API to finish responding (up to 30s)
            var tcs = new TaskCompletionSource<string>();
            void onResponse(string transcript) => tcs.TrySetResult(transcript);
            SpeakResponseReady += onResponse;

            SpeakRequested?.Invoke(textProp.GetString()!);

            var timeout = Task.Delay(TimeSpan.FromSeconds(30));
            var completed = await Task.WhenAny(tcs.Task, timeout);

            SpeakResponseReady -= onResponse;

            if (completed == tcs.Task)
            {
                return Results.Ok(new { status = "ok", transcript = tcs.Task.Result });
            }

            return Results.Ok(new { status = "ok", transcript = "(no response within 30s)" });
        }
    }

    private async Task<IResult> HandleAvatar(HttpContext context)
    {
        var body = await ReadBodyWithSizeLimitAsync(context);
        if (body is null)
            return Results.StatusCode(413);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "malformed JSON" });
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("expression", out var exprProp)
                || exprProp.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(exprProp.GetString()))
            {
                return Results.BadRequest(new { error = "missing required field: expression" });
            }

            AvatarRequested?.Invoke(exprProp.GetString()!);
        }

        return Results.Ok(new { status = "ok" });
    }

    private IResult HandleTalkMode()
    {
        TalkModeToggleRequested?.Invoke();
        return Results.Ok(new { status = "ok" });
    }

    /// <summary>Read request body, rejecting payloads over MaxBodySize.</summary>
    private static async Task<string?> ReadBodyWithSizeLimitAsync(HttpContext context)
    {
        context.Request.EnableBuffering();
        if (context.Request.ContentLength > MaxBodySize)
            return null;

        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();

        if (body.Length > MaxBodySize)
            return null;

        return body;
    }

    /// <summary>Write a single SSE event to the response stream.</summary>
    private static async Task WriteSseEventAsync(HttpResponse response, string eventType, string data)
    {
        await response.WriteAsync($"event: {eventType}\n");
        await response.WriteAsync($"data: {data}\n\n");
        await response.Body.FlushAsync();
    }
}
