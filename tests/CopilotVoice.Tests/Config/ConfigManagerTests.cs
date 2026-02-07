using CopilotVoice.Config;

namespace CopilotVoice.Tests.Config;

public class ConfigManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configFile;
    private readonly ConfigManager _sut;

    public ConfigManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"copilot-voice-test-{Guid.NewGuid()}");
        _configFile = Path.Combine(_tempDir, "config.json");

        // Use reflection to override the private static ConfigDir/ConfigFile fields
        var dirField = typeof(ConfigManager).GetField("ConfigDir",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var fileField = typeof(ConfigManager).GetField("ConfigFile",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        dirField.SetValue(null, _tempDir);
        fileField.SetValue(null, _configFile);

        _sut = new ConfigManager();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void LoadConfig_ReturnsDefaults_WhenFileNotExists()
    {
        var config = _sut.Load();

        Assert.Equal(AuthMode.SignIn, config.AuthMode);
        Assert.Null(config.AzureSpeechKey);
        Assert.Equal("centralus", config.AzureSpeechRegion);
        Assert.Equal("Ctrl+Shift+V", config.Hotkey);
        Assert.Equal("en-US", config.Language);
        Assert.True(config.ShowRecordingIndicator);
        Assert.True(config.AutoPressEnter);
        Assert.False(config.PlayConfirmationSound);
        Assert.True(config.StartOnLogin);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_AllFields()
    {
        var original = new AppConfig
        {
            AuthMode = AuthMode.ApiKey,
            AzureSpeechKey = "test-key-123",
            AzureSpeechRegion = "eastus",
            AzureResourceName = "my-resource",
            Hotkey = "Alt+R",
            Language = "pt-BR",
            DefaultSessionId = "session-42",
            ShowRecordingIndicator = false,
            AutoPressEnter = false,
            PlayConfirmationSound = true,
            StartOnLogin = false
        };

        _sut.Save(original);
        var loaded = _sut.Load();

        Assert.Equal(original.AuthMode, loaded.AuthMode);
        Assert.Equal(original.AzureSpeechKey, loaded.AzureSpeechKey);
        Assert.Equal(original.AzureSpeechRegion, loaded.AzureSpeechRegion);
        Assert.Equal(original.AzureResourceName, loaded.AzureResourceName);
        Assert.Equal(original.Hotkey, loaded.Hotkey);
        Assert.Equal(original.Language, loaded.Language);
        Assert.Equal(original.DefaultSessionId, loaded.DefaultSessionId);
        Assert.Equal(original.ShowRecordingIndicator, loaded.ShowRecordingIndicator);
        Assert.Equal(original.AutoPressEnter, loaded.AutoPressEnter);
        Assert.Equal(original.PlayConfirmationSound, loaded.PlayConfirmationSound);
        Assert.Equal(original.StartOnLogin, loaded.StartOnLogin);
    }

    [Fact]
    public void CliArgs_Parse_AllFlags()
    {
        var args = new[]
        {
            "--key", "my-key",
            "--region", "westus2",
            "--hotkey", "Alt+X",
            "--session", "sess-1",
            "--list-sessions",
            "--help"
        };

        var cli = CliArgs.Parse(args);

        Assert.Equal("my-key", cli.Key);
        Assert.Equal("westus2", cli.Region);
        Assert.Equal("Alt+X", cli.Hotkey);
        Assert.Equal("sess-1", cli.SessionId);
        Assert.True(cli.ListSessions);
        Assert.True(cli.ShowHelp);
    }

    [Fact]
    public void CliArgs_ApplyOverrides_SetsConfigValues()
    {
        var cli = new CliArgs
        {
            Key = "override-key",
            Region = "northeurope",
            Hotkey = "Ctrl+M",
            SessionId = "override-session"
        };
        var config = new AppConfig();

        cli.ApplyOverrides(config);

        Assert.Equal("override-key", config.AzureSpeechKey);
        Assert.Equal(AuthMode.ApiKey, config.AuthMode);
        Assert.Equal("northeurope", config.AzureSpeechRegion);
        Assert.Equal("Ctrl+M", config.Hotkey);
        Assert.Equal("override-session", config.DefaultSessionId);
    }
}
