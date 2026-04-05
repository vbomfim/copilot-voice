namespace CopilotVoice.Voice;

/// <summary>
/// Factory for creating Voice Live API sessions.
/// Manages WebSocket connection establishment and authentication.
/// </summary>
public interface IVoiceLiveClient
{
    /// <summary>
    /// Connect to the Azure Voice Live API and return an active session.
    /// The session is configured with the provided config (model, voice, instructions).
    /// </summary>
    Task<IVoiceLiveSession> ConnectAsync(VoiceLiveConfig config, CancellationToken ct = default);
}
