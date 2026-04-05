using System.Runtime.CompilerServices;
using System.Threading.Channels;
using CopilotVoice.Voice;

namespace CopilotVoice.Tests.Voice;

/// <summary>
/// In-memory test double for IRealtimeConnection.
/// Uses channels for bidirectional message passing between test code and session.
/// </summary>
internal sealed class FakeRealtimeConnection : IRealtimeConnection
{
    private readonly Channel<string> _serverToClient = Channel.CreateUnbounded<string>();
    private readonly Channel<string> _clientToServer = Channel.CreateUnbounded<string>();
    private bool _connected;
    private bool _disposed;

    public bool IsConnected => _connected && !_disposed;

    /// <summary>URI passed to ConnectAsync, captured for assertions.</summary>
    public Uri? ConnectedUri { get; private set; }

    /// <summary>Headers passed to ConnectAsync, captured for assertions.</summary>
    public IDictionary<string, string>? ConnectedHeaders { get; private set; }

    /// <summary>How many times ConnectAsync was called (for reconnection testing).</summary>
    public int ConnectCallCount { get; private set; }

    // --- Test helpers ---

    /// <summary>Enqueue a server event for the session to receive.</summary>
    public async Task EnqueueServerEventAsync(string json)
    {
        await _serverToClient.Writer.WriteAsync(json);
    }

    /// <summary>Read the next event sent by the session to the server.</summary>
    public async Task<string> ReadClientEventAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));
        return await _clientToServer.Reader.ReadAsync(cts.Token);
    }

    /// <summary>Try to read a client event; returns null if no event within timeout.</summary>
    public async Task<string?> TryReadClientEventAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromMilliseconds(200));
        try
        {
            return await _clientToServer.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Signal server-side close (the receive enumerable will complete).</summary>
    public void CompleteServerStream()
    {
        _serverToClient.Writer.TryComplete();
        _connected = false;
    }

    // --- IRealtimeConnection implementation ---

    public Task ConnectAsync(Uri uri, IDictionary<string, string> headers, CancellationToken ct)
    {
        ConnectedUri = uri;
        ConnectedHeaders = headers;
        ConnectCallCount++;
        _connected = true;
        return Task.CompletedTask;
    }

    public async Task SendEventAsync(string eventJson, CancellationToken ct)
    {
        if (!_connected)
            throw new InvalidOperationException("Not connected.");

        await _clientToServer.Writer.WriteAsync(eventJson, ct);
    }

    public async IAsyncEnumerable<string> ReceiveEventsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var msg in _serverToClient.Reader.ReadAllAsync(ct))
        {
            yield return msg;
        }
    }

    public Task CloseAsync(CancellationToken ct)
    {
        _connected = false;
        _serverToClient.Writer.TryComplete();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _connected = false;
        _serverToClient.Writer.TryComplete();
        _clientToServer.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
