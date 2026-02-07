using System.Text.Json.Serialization;

namespace CopilotVoice.Config;

public class AppConfig
{
    // Authentication
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AuthMode AuthMode { get; set; } = AuthMode.SignIn;
    public string? AzureSpeechKey { get; set; }
    public string AzureSpeechRegion { get; set; } = "centralus";
    public string? AzureResourceName { get; set; }

    // Push-to-talk
    public string Hotkey { get; set; } = "Ctrl+Shift+V";
    public string Language { get; set; } = "en-US";

    // Session
    public string? DefaultSessionId { get; set; }

    // Behavior
    public bool ShowRecordingIndicator { get; set; } = true;
    public bool AutoPressEnter { get; set; } = true;
    public bool PlayConfirmationSound { get; set; } = false;
    public bool StartOnLogin { get; set; } = true;
}

public enum AuthMode
{
    SignIn,
    ApiKey,
    Env
}
