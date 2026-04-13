namespace CopilotVoice.Voice;

/// <summary>
/// Configuration for connecting to the Azure Voice Live (OpenAI Realtime) API.
/// </summary>
public record VoiceLiveConfig(
    string Endpoint,
    string? ApiKey = null,
    string Model = "gpt-realtime",
    string Voice = "alloy",
    string SystemInstructions = ""
);
