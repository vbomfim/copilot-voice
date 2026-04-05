using System.Runtime.CompilerServices;

namespace CopilotVoice.Voice;

/// <summary>
/// Internal abstraction over the raw WebSocket transport.
/// Enables unit testing of VoiceLiveSession without a real WebSocket.
/// </summary>
internal interface IRealtimeConnection : IAsyncDisposable
{
    /// <summary>Establish the WebSocket connection.</summary>
    Task ConnectAsync(Uri uri, IDictionary<string, string> headers, CancellationToken ct);

    /// <summary>Send a JSON event string over the WebSocket.</summary>
    Task SendEventAsync(string eventJson, CancellationToken ct);

    /// <summary>
    /// Yield received JSON event strings until the connection closes or is cancelled.
    /// The enumerable completes (without error) when the server closes the connection.
    /// </summary>
    IAsyncEnumerable<string> ReceiveEventsAsync(CancellationToken ct);

    /// <summary>Whether the WebSocket is currently connected.</summary>
    bool IsConnected { get; }

    /// <summary>Close the WebSocket gracefully.</summary>
    Task CloseAsync(CancellationToken ct);
}
