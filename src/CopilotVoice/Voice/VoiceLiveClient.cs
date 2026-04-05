namespace CopilotVoice.Voice;

/// <summary>
/// Creates Voice Live API sessions over WebSocket.
/// Resolves credentials from environment variables, config, or Azure Identity.
/// </summary>
public sealed class VoiceLiveClient : IVoiceLiveClient
{
    /// <summary>
    /// Connect to the Voice Live API and return an active, configured session.
    /// </summary>
    public async Task<IVoiceLiveSession> ConnectAsync(VoiceLiveConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.Endpoint))
            throw new ArgumentException("Endpoint must be provided.", nameof(config));

        var resolvedConfig = ResolveCredentials(config);
        var connection = CreateConnection();
        var session = new VoiceLiveSession(resolvedConfig, connection, CreateConnection);

        await session.StartAsync(ct).ConfigureAwait(false);
        return session;
    }

    /// <summary>
    /// Resolve credentials using the priority chain:
    /// 1. Config values (explicit ApiKey/Endpoint)
    /// 2. Environment variables (AZURE_VOICELIVE_KEY / AZURE_VOICELIVE_ENDPOINT)
    /// 3. Falls through to DefaultAzureCredential (handled at connection time via bearer token)
    /// </summary>
    internal static VoiceLiveConfig ResolveCredentials(VoiceLiveConfig config)
    {
        var endpoint = config.Endpoint;
        var apiKey = config.ApiKey;

        // Environment variable overrides
        var envEndpoint = Environment.GetEnvironmentVariable("AZURE_VOICELIVE_ENDPOINT");
        if (!string.IsNullOrEmpty(envEndpoint) && string.IsNullOrEmpty(endpoint))
            endpoint = envEndpoint;

        var envKey = Environment.GetEnvironmentVariable("AZURE_VOICELIVE_KEY");
        if (!string.IsNullOrEmpty(envKey) && string.IsNullOrEmpty(apiKey))
            apiKey = envKey;

        return config with { Endpoint = endpoint, ApiKey = apiKey };
    }

    private static IRealtimeConnection CreateConnection() => new WebSocketConnection();
}
