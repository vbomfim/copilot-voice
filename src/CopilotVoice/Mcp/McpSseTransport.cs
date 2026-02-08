using System.Net;
using System.Text.Json;

namespace CopilotVoice.Mcp;

/// <summary>
/// HTTP transport for MCP using Server-Sent Events (SSE).
/// 
/// Protocol:
/// 1. Client GET /sse → server sends "event: endpoint\ndata: /message?sessionId=xxx"
/// 2. Client POST /message?sessionId=xxx → JSON-RPC request body
/// 3. Server sends "event: message\ndata: {json-rpc}" on the SSE stream
/// </summary>
public class McpSseTransport : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly McpServer _mcpServer;
    private readonly int _port;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, SseSession> _sessions = new();
    private readonly object _lock = new();

    public event Action<string>? OnLog;

    public McpSseTransport(McpServer mcpServer, int port = 7702)
    {
        _mcpServer = mcpServer;
        _port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public void Start()
    {
        _listener.Start();
        Log($"MCP SSE server listening on http://localhost:{_port}");
        _ = AcceptRequestsAsync(_cts.Token);
    }

    private async Task AcceptRequestsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(ct);
                _ = HandleRequestAsync(context, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            catch (Exception ex)
            {
                Log($"Accept error: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        var path = context.Request.Url?.AbsolutePath ?? "";
        var method = context.Request.HttpMethod;

        try
        {
            switch (path)
            {
                case "/sse" when method == "GET":
                    await HandleSseConnectionAsync(context, ct);
                    break;
                case "/message" when method == "POST":
                    await HandleMessageAsync(context);
                    break;
                default:
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"Request error: {ex.Message}");
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch { }
        }
    }

    private async Task HandleSseConnectionAsync(HttpListenerContext context, CancellationToken ct)
    {
        var sessionId = Guid.NewGuid().ToString("N")[..12];
        Log($"SSE connection opened, sessionId={sessionId}");

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.Add("Cache-Control", "no-cache");
        context.Response.Headers.Add("Connection", "keep-alive");
        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        context.Response.StatusCode = 200;

        var outputStream = context.Response.OutputStream;
        var writer = new StreamWriter(outputStream) { AutoFlush = true };

        // Create pipe for reading client messages (POST → pipe → McpClientConnection reader)
        var pipe = new System.IO.Pipelines.Pipe();
        var pipeReader = new StreamReader(pipe.Reader.AsStream());
        var sseWriter = new SseStreamWriter(writer);

        var session = new SseSession
        {
            SessionId = sessionId,
            Writer = sseWriter,
            PipeWriter = pipe.Writer,
            Context = context,
        };

        lock (_lock) { _sessions[sessionId] = session; }

        // Send the endpoint event — tells the client where to POST messages
        await writer.WriteAsync($"event: endpoint\ndata: /message?sessionId={sessionId}\n\n");
        await writer.FlushAsync();

        // Register with McpServer using the pipe reader (for input) and SSE writer (for output)
        var connection = await _mcpServer.AddClientAsync(pipeReader, sseWriter, ct);
        session.Connection = connection;

        // Keep SSE connection alive until disconnection
        var tcs = new TaskCompletionSource();
        connection.OnDisconnected += _ =>
        {
            tcs.TrySetResult();
            lock (_lock) { _sessions.Remove(sessionId); }
        };

        try
        {
            await tcs.Task.WaitAsync(ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            Log($"SSE connection closed, sessionId={sessionId}");
            try { context.Response.Close(); } catch { }
        }
    }

    private async Task HandleMessageAsync(HttpListenerContext context)
    {
        var sessionId = context.Request.QueryString["sessionId"];
        if (string.IsNullOrEmpty(sessionId))
        {
            context.Response.StatusCode = 400;
            context.Response.Close();
            return;
        }

        SseSession? session;
        lock (_lock) { _sessions.TryGetValue(sessionId, out session); }

        if (session == null)
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
            return;
        }

        using var reader = new StreamReader(context.Request.InputStream);
        var body = await reader.ReadToEndAsync();

        // Write the JSON-RPC message into the pipe for McpClientConnection to read
        var bytes = System.Text.Encoding.UTF8.GetBytes(body + "\n");
        await session.PipeWriter.WriteAsync(bytes);
        await session.PipeWriter.FlushAsync();

        context.Response.StatusCode = 202;
        context.Response.Close();
    }

    private void Log(string msg) => OnLog?.Invoke($"[SSE] {msg}");

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        lock (_lock) { _sessions.Clear(); }
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    private class SseSession
    {
        public required string SessionId { get; init; }
        public required SseStreamWriter Writer { get; init; }
        public required System.IO.Pipelines.PipeWriter PipeWriter { get; init; }
        public required HttpListenerContext Context { get; init; }
        public McpClientConnection? Connection { get; set; }
    }
}

/// <summary>
/// TextWriter that wraps output in SSE "event: message" format.
/// Each WriteLine call becomes: "event: message\ndata: {line}\n\n"
/// </summary>
public class SseStreamWriter : TextWriter
{
    private readonly StreamWriter _inner;
    private readonly object _writeLock = new();

    public SseStreamWriter(StreamWriter inner) => _inner = inner;

    public override System.Text.Encoding Encoding => _inner.Encoding;

    public override void WriteLine(string? value)
    {
        if (value == null) return;
        lock (_writeLock)
        {
            _inner.Write($"event: message\ndata: {value}\n\n");
            _inner.Flush();
        }
    }

    public override async Task WriteLineAsync(string? value)
    {
        if (value == null) return;
        // Use sync lock since we need atomic writes
        lock (_writeLock)
        {
            _inner.Write($"event: message\ndata: {value}\n\n");
            _inner.Flush();
        }
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync() => _inner.FlushAsync();
}
