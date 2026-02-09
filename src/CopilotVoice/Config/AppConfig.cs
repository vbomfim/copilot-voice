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
    public List<string>? CustomPhrases { get; set; }

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

    /// <summary>Curated list of popular Azure Neural TTS voices.</summary>
    public static readonly (string Name, string Label)[] AvailableVoices = new[]
    {
        ("en-US-JennyNeural", "Jenny (US)"),
        ("en-US-AriaNeural", "Aria (US)"),
        ("en-US-GuyNeural", "Guy (US)"),
        ("en-US-DavisNeural", "Davis (US)"),
        ("en-US-SaraNeural", "Sara (US)"),
        ("en-US-AndrewNeural", "Andrew (US)"),
        ("en-US-EmmaNeural", "Emma (US)"),
        ("en-GB-SoniaNeural", "Sonia (UK)"),
        ("en-GB-RyanNeural", "Ryan (UK)"),
        ("en-AU-NatashaNeural", "Natasha (AU)"),
        ("en-AU-WilliamNeural", "William (AU)"),
        ("pt-BR-FranciscaNeural", "Francisca (BR)"),
        ("pt-BR-AntonioNeural", "Antonio (BR)"),
        ("es-ES-ElviraNeural", "Elvira (ES)"),
        ("fr-FR-DeniseNeural", "Denise (FR)"),
        ("de-DE-KatjaNeural", "Katja (DE)"),
        ("ja-JP-NanamiNeural", "Nanami (JP)"),
        ("zh-CN-XiaoxiaoNeural", "Xiaoxiao (CN)"),
    };

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
