namespace CopilotVoice.Config;

public class AzureAuthProvider
{
    /// <summary>
    /// Resolves Azure Speech key using priority: CLI arg > env var > config file > sign-in
    /// </summary>
    public (string key, string region) Resolve(AppConfig config)
    {
        var key = ResolveKey(config);
        var region = ResolveRegion(config);
        return (key, region);
    }

    public string ResolveKey(AppConfig config)
    {
        // 1. Config value (set by CLI --key arg override)
        if (config.AuthMode == AuthMode.ApiKey && !string.IsNullOrEmpty(config.AzureSpeechKey))
            return config.AzureSpeechKey;

        // 2. Environment variable
        var envKey = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
        if (!string.IsNullOrEmpty(envKey))
            return envKey;

        // 3. Config file value (non-CLI)
        if (!string.IsNullOrEmpty(config.AzureSpeechKey))
            return config.AzureSpeechKey;

        // 4. Sign-in flow (interactive)
        if (config.AuthMode == AuthMode.SignIn)
            return GetTokenFromSignIn(config);

        throw new InvalidOperationException(
            "No Azure Speech credentials found. Set AZURE_SPEECH_KEY environment variable, " +
            "use --key argument, or configure sign-in with --auth signin");
    }

    public string ResolveRegion(AppConfig config)
    {
        var envRegion = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION");
        return envRegion ?? config.AzureSpeechRegion;
    }

    private string GetTokenFromSignIn(AppConfig config)
    {
        // TODO: Implement Azure.Identity InteractiveBrowserCredential flow
        // For now, throw with helpful message
        throw new NotImplementedException(
            "Microsoft Sign-In authentication is not yet implemented. " +
            "Please use --key <key> or set AZURE_SPEECH_KEY environment variable.");
    }
}
