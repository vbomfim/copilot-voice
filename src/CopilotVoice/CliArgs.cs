namespace CopilotVoice;

public class CliArgs
{
    public string? Key { get; set; }
    public string? Region { get; set; }
    public string? Hotkey { get; set; }
    public bool ShowHelp { get; set; }

    public static CliArgs Parse(string[] args)
    {
        var result = new CliArgs();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--key" when i + 1 < args.Length:
                    result.Key = args[++i];
                    break;
                case "--region" when i + 1 < args.Length:
                    result.Region = args[++i];
                    break;
                case "--hotkey" when i + 1 < args.Length:
                    result.Hotkey = args[++i];
                    break;
                case "--help" or "-h":
                    result.ShowHelp = true;
                    break;
            }
        }
        return result;
    }

    public void ApplyOverrides(Config.AppConfig config)
    {
        if (Key != null)
        {
            config.AzureSpeechKey = Key;
            config.AuthMode = Config.AuthMode.ApiKey;
        }
        if (Region != null)
            config.AzureSpeechRegion = Region;
        if (Hotkey != null)
            config.Hotkey = Hotkey;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            Copilot Voice - Push-to-talk voice input for GitHub Copilot CLI

            Usage: copilot-voice [options]

            Options:
              --key <key>         Azure Speech subscription key
              --region <region>   Azure Speech region (default: centralus)
              --hotkey <combo>    Push-to-talk hotkey (default: Alt+Space)
              --help, -h          Show this help message

            Environment variables:
              AZURE_SPEECH_KEY          Azure Speech subscription key
              AZURE_SPEECH_REGION       Azure Speech region
              AZURE_VOICELIVE_ENDPOINT  Voice Live API endpoint
              AZURE_VOICELIVE_KEY       Voice Live API key
            """);
    }
}
