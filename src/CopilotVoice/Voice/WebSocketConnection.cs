using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace CopilotVoice.Voice;

/// <summary>
/// Real WebSocket implementation of IRealtimeConnection using ClientWebSocket.
/// </summary>
internal sealed class WebSocketConnection : IRealtimeConnection
{
    private ClientWebSocket? _ws;
    private const int ReceiveBufferSize = 16 * 1024; // 16 KB

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public async Task ConnectAsync(Uri uri, IDictionary<string, string> headers, CancellationToken ct)
    {
        _ws = new ClientWebSocket();

        foreach (var (key, value) in headers)
            _ws.Options.SetRequestHeader(key, value);

        await _ws.ConnectAsync(uri, ct).ConfigureAwait(false);
    }

    public async Task SendEventAsync(string eventJson, CancellationToken ct)
    {
        if (_ws is null || _ws.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket is not connected.");

        var bytes = Encoding.UTF8.GetBytes(eventJson);
        await _ws.SendAsync(
            bytes.AsMemory(),
            WebSocketMessageType.Text,
            endOfMessage: true,
            ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<string> ReceiveEventsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (_ws is null)
            yield break;

        var buffer = new byte[ReceiveBufferSize];
        var messageBuffer = new StringBuilder();

        while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            ValueWebSocketReceiveResult result;
            try
            {
                result = await _ws.ReceiveAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                yield break;
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                yield break;

            messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (result.EndOfMessage)
            {
                yield return messageBuffer.ToString();
                messageBuffer.Clear();
            }
        }
    }

    public async Task CloseAsync(CancellationToken ct)
    {
        if (_ws is null || _ws.State != WebSocketState.Open)
            return;

        try
        {
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session ended", ct)
                .ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            // Already closed — ignore
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_ws is not null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                    await CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort close
            }
            finally
            {
                _ws.Dispose();
                _ws = null;
            }
        }
    }
}
