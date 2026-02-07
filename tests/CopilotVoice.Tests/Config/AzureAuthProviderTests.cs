using CopilotVoice.Config;

namespace CopilotVoice.Tests.Config;

[Collection("EnvVarTests")]
public class AzureAuthProviderTests : IDisposable
{
    private readonly AzureAuthProvider _sut = new();
    private readonly string? _savedKey;
    private readonly string? _savedRegion;

    public AzureAuthProviderTests()
    {
        // Preserve existing env vars and clear them for isolation
        _savedKey = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
        _savedRegion = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION");
        Environment.SetEnvironmentVariable("AZURE_SPEECH_KEY", null);
        Environment.SetEnvironmentVariable("AZURE_SPEECH_REGION", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("AZURE_SPEECH_KEY", _savedKey);
        Environment.SetEnvironmentVariable("AZURE_SPEECH_REGION", _savedRegion);
    }

    [Fact]
    public void ResolveKey_ReturnsConfigKey_WhenApiKeyMode()
    {
        var config = new AppConfig
        {
            AuthMode = AuthMode.ApiKey,
            AzureSpeechKey = "my-api-key"
        };

        var key = _sut.ResolveKey(config);

        Assert.Equal("my-api-key", key);
    }

    [Fact]
    public void ResolveKey_ThrowsClear_WhenNoCredentials()
    {
        var config = new AppConfig
        {
            AuthMode = AuthMode.Env,
            AzureSpeechKey = null
        };

        var ex = Assert.Throws<InvalidOperationException>(() => _sut.ResolveKey(config));
        Assert.Contains("No Azure Speech credentials found", ex.Message);
    }

    [Fact]
    public void ResolveRegion_ReturnsConfiguredValue()
    {
        var config = new AppConfig { AzureSpeechRegion = "centralus" };

        var region = _sut.ResolveRegion(config);

        Assert.Equal("centralus", region);
    }

    [Fact]
    public void ResolveKey_EnvVarTakesPriority_OverConfigKey()
    {
        Environment.SetEnvironmentVariable("AZURE_SPEECH_KEY", "env-key");
        var config = new AppConfig
        {
            AuthMode = AuthMode.Env,
            AzureSpeechKey = "config-key"
        };

        var key = _sut.ResolveKey(config);

        Assert.Equal("env-key", key);
    }
}
