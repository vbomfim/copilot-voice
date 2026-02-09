namespace CopilotVoice;

public class CliArgs
{
    public string? Key { get; set; }
    public string? Region { get; set; }
    public string? Hotkey { get; set; }
    public string? SessionId { get; set; }
    public bool ListSessions { get; set; }
    public bool RegisterSession { get; set; }
    public string? RegisterLabel { get; set; }
    public bool McpMode { get; set; }
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
                case "--session" when i + 1 < args.Length:
                    result.SessionId = args[++i];
                    break;
                case "--list-sessions":
                    result.ListSessions = true;
                    break;
                case "--register":
                    result.RegisterSession = true;
                    break;
                case "--label" when i + 1 < args.Length:
                    result.RegisterLabel = args[++i];
                    break;
                case "--mcp":
                    result.McpMode = true;
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
        if (SessionId != null)
            config.DefaultSessionId = SessionId;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            Copilot Voice - Push-to-talk voice input for GitHub Copilot CLI

            Usage: copilot-voice [options]

            Options:
              --key <key>         Azure Speech subscription key
              --region <region>   Azure Speech region (default: centralus)
              --hotkey <combo>    Push-to-talk hotkey (default: Ctrl+Shift+V)
              --session <id>      Target a specific session
              --list-sessions     List active Copilot CLI sessions and exit
              --register          Register current terminal as a Copilot CLI session
              --label <name>      Custom label for --register (default: terminal window title)
              --mcp               Run as MCP server (stdio JSON-RPC)
              --help, -h          Show this help message

            Environment variables:
              AZURE_SPEECH_KEY    Azure Speech subscription key
              AZURE_SPEECH_REGION Azure Speech region

            Auth priority: --key flag > AZURE_SPEECH_KEY env var > config file > Microsoft Sign-In
            """);
    }
}
