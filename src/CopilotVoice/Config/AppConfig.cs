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
    public string Hotkey { get; set; } = "Alt+Space";
    public string Language { get; set; } = "en-US";

    // Session
    public string? DefaultSessionId { get; set; }

    // Behavior
    public bool ShowRecordingIndicator { get; set; } = true;
    public bool AutoPressEnter { get; set; } = true;
    public bool PlayConfirmationSound { get; set; } = false;
    public bool StartOnLogin { get; set; } = true;

    // Text-to-Speech
    public bool EnableVoiceOutput { get; set; } = true;
    public string VoiceName { get; set; } = "en-US-JennyNeural";
    public double VoiceSpeed { get; set; } = 1.0;

    // Avatar
    public bool ShowAvatar { get; set; } = true;
    public string AvatarTheme { get; set; } = "robot";
    public int BlinkIntervalSeconds { get; set; } = 8;
    public bool IdleAnimations { get; set; } = true;

    // Pomodoro
    public int PomodoroWorkMinutes { get; set; } = 25;
    public int PomodoroBreakMinutes { get; set; } = 5;
    public bool PomodoroVoiceAlerts { get; set; } = true;
}

public enum AuthMode
{
    SignIn,
    ApiKey,
    Env
}
