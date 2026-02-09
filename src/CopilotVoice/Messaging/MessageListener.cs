using System.Net;
using System.Text;
using System.Text.Json;

namespace CopilotVoice.Messaging;

public class MessageListener : IDisposable
{
    private readonly HttpListener _listener;
    private readonly MessageQueue _queue;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private Task? _processTask;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public event Action<InboundMessage>? OnMessageReceived;
    public event Action<InboundMessage>? OnBubbleReceived;
    public event Func<RegisterRequest, Task<string>>? OnRegisterReceived;

    public MessageListener(int port = 7701)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _queue = new MessageQueue();
    }

    public void Start()
    {
        if (_cts is not null)
            return;

        _cts = new CancellationTokenSource();
        _listener.Start();

        _processTask = _queue.ProcessAsync(msg => OnMessageReceived?.Invoke(msg), _cts.Token);
        _listenTask = AcceptRequestsAsync(_cts.Token);
    }

    public void Stop()
    {
        if (_cts is null)
            return;

        _cts.Cancel();
        _listener.Stop();
        _queue.Complete();

        try { _listenTask?.Wait(); } catch { /* expected on cancel */ }
        try { _processTask?.Wait(); } catch { /* expected on cancel */ }

        _cts.Dispose();
        _cts = null;
    }

    private async Task AcceptRequestsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }

            _ = HandleRequestAsync(context);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            if (request.HttpMethod == "GET" && request.Url?.AbsolutePath == "/health")
            {
                await WriteResponse(response, 200, """{"status":"ok"}""");
                return;
            }

            if (request.HttpMethod == "POST" && request.Url?.AbsolutePath == "/speak")
            {
                await HandleSpeak(request, response);
                return;
            }

            if (request.HttpMethod == "POST" && request.Url?.AbsolutePath == "/bubble")
            {
                await HandleBubble(request, response);
                return;
            }

            if (request.HttpMethod == "POST" && request.Url?.AbsolutePath == "/register")
            {
                await HandleRegister(request, response);
                return;
            }

            await WriteResponse(response, 404, """{"error":"not found"}""");
        }
        catch
        {
            try { await WriteResponse(response, 500, """{"error":"internal error"}"""); }
            catch { /* best effort */ }
        }
    }

    /// <summary>Called when /speak completes TTS. Set by AppServices.</summary>
    public event Func<InboundMessage, Task>? OnSpeakReceived;

    private async Task HandleSpeak(HttpListenerRequest request, HttpListenerResponse response)
    {
        string body;
        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
        {
            body = await reader.ReadToEndAsync();
        }

        InboundMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<InboundMessage>(body, JsonOptions);
        }
        catch (JsonException)
        {
            await WriteResponse(response, 400, """{"error":"malformed JSON"}""");
            return;
        }

        if (message is null || string.IsNullOrWhiteSpace(message.Text))
        {
            await WriteResponse(response, 400, """{"error":"missing required field: text"}""");
            return;
        }

        if (message.Timestamp == default)
            message.Timestamp = DateTime.UtcNow;

        // Synchronous: wait for TTS to complete before responding
        if (OnSpeakReceived != null)
        {
            try
            {
                await OnSpeakReceived(message);
                await WriteResponse(response, 200, """{"status":"ok"}""");
            }
            catch (Exception ex)
            {
                await WriteResponse(response, 500, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }
        else
        {
            _queue.Enqueue(message);
            await WriteResponse(response, 202, """{"status":"queued"}""");
        }
    }

    private async Task HandleBubble(HttpListenerRequest request, HttpListenerResponse response)
    {
        string body;
        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
        {
            body = await reader.ReadToEndAsync();
        }

        InboundMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<InboundMessage>(body, JsonOptions);
        }
        catch (JsonException)
        {
            await WriteResponse(response, 400, """{"error":"malformed JSON"}""");
            return;
        }

        if (message is null || string.IsNullOrWhiteSpace(message.Text))
        {
            await WriteResponse(response, 400, """{"error":"missing required field: text"}""");
            return;
        }

        OnBubbleReceived?.Invoke(message);
        await WriteResponse(response, 200, """{"status":"ok"}""");
    }

    private async Task HandleRegister(HttpListenerRequest request, HttpListenerResponse response)
    {
        string body;
        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
        {
            body = await reader.ReadToEndAsync();
        }

        RegisterRequest? reg;
        try
        {
            reg = JsonSerializer.Deserialize<RegisterRequest>(body, JsonOptions);
        }
        catch (JsonException)
        {
            await WriteResponse(response, 400, """{"error":"malformed JSON"}""");
            return;
        }

        if (reg is null || reg.Pid <= 0)
        {
            await WriteResponse(response, 400, """{"error":"missing required field: pid"}""");
            return;
        }

        if (OnRegisterReceived != null)
        {
            try
            {
                var result = await OnRegisterReceived(reg);
                var responseObj = JsonSerializer.Serialize(new { status = "registered", label = result });
                await WriteResponse(response, 200, responseObj);
            }
            catch (Exception ex)
            {
                var errorObj = JsonSerializer.Serialize(new { error = ex.Message });
                await WriteResponse(response, 500, errorObj);
            }
        }
        else
        {
            await WriteResponse(response, 503, """{"error":"registration not available"}""");
        }
    }

    private static async Task WriteResponse(HttpListenerResponse response, int statusCode, string json)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        var buffer = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.Close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        (_listener as IDisposable).Dispose();
        GC.SuppressFinalize(this);
    }
}
