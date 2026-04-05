namespace CopilotVoice.Voice;

/// <summary>
/// An active Voice Live API session. Supports bidirectional audio streaming,
/// text input, function call handling, and session reconfiguration.
/// Dispose to close the WebSocket connection gracefully.
/// </summary>
public interface IVoiceLiveSession : IAsyncDisposable
{
    /// <summary>Stream PCM16 audio to the API via input_audio_buffer.append.</summary>
    Task SendAudioAsync(ReadOnlyMemory<byte> pcm16Audio, CancellationToken ct = default);

    /// <summary>Signal end of user audio input via input_audio_buffer.commit.</summary>
    Task CommitAudioAsync(CancellationToken ct = default);

    /// <summary>Send a text message to the conversation.</summary>
    Task SendTextAsync(string text, CancellationToken ct = default);

    /// <summary>Update session configuration (voice, instructions, tools, etc.).</summary>
    Task UpdateSessionAsync(SessionUpdate update, CancellationToken ct = default);

    /// <summary>Send the result of a function call back to the API.</summary>
    Task SendFunctionResultAsync(string callId, string result, CancellationToken ct = default);

    /// <summary>Fired when a voice response audio chunk is received (PCM16 bytes).</summary>
    event Action<ReadOnlyMemory<byte>>? AudioReceived;

    /// <summary>Fired when a text transcript delta is received.</summary>
    event Action<string>? TranscriptReceived;

    /// <summary>Fired when the model requests a function call.</summary>
    event Action<FunctionCall>? FunctionCallReceived;

    /// <summary>Fired when the model's response is fully complete (all audio/text/function calls delivered).</summary>
    event Action? ResponseDone;

    /// <summary>Fired on API error.</summary>
    event Action<string>? ErrorReceived;

    /// <summary>Fired when the session is configured and ready for audio.</summary>
    event Action? SessionReady;

    /// <summary>Fired when the WebSocket disconnects (after all reconnection attempts exhausted).</summary>
    event Action? Disconnected;
}
