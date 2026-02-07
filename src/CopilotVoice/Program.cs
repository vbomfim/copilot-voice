using CopilotVoice.Config;

namespace CopilotVoice;

class Program
{
    static async Task Main(string[] args)
    {
        var cliArgs = CliArgs.Parse(args);

        if (cliArgs.ShowHelp) { CliArgs.PrintHelp(); return; }

        // Load or create config
        var configManager = new ConfigManager();
        var config = configManager.LoadOrCreate();
        cliArgs.ApplyOverrides(config);

        // Reusable session detector
        var sessionDetector = new Sessions.SessionDetector();

        // Handle one-shot commands
        if (cliArgs.ListSessions)
        {
            var sessions = sessionDetector.DetectSessions();
            if (sessions.Count == 0)
            {
                Console.WriteLine("No active Copilot CLI sessions found.");
                return;
            }
            foreach (var s in sessions)
                Console.WriteLine($"  {s.TerminalApp} — {System.IO.Path.GetFileName(s.WorkingDirectory)} (PID: {s.ProcessId})");
            return;
        }

        Console.WriteLine("🎤🤖 Copilot Voice — Starting...");

        // Resolve Azure credentials
        var authProvider = new AzureAuthProvider();
        try
        {
            var (_, region) = authProvider.Resolve(config);
            Console.WriteLine($"  ✅ Azure Speech: {region}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️  Auth: {ex.Message}");
            Console.WriteLine("  Run with --key <key> or set AZURE_SPEECH_KEY");
            return;
        }

        // Detect sessions
        var initialSessions = sessionDetector.DetectSessions();
        Console.WriteLine($"  📡 Found {initialSessions.Count} session(s)");

        // Initialize hotkey
        Hotkey.HotkeyListener hotkey;
        try { hotkey = new Hotkey.HotkeyListener(config.Hotkey); }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"  ⚠️  Invalid hotkey \"{config.Hotkey}\": {ex.Message}");
            Console.WriteLine("  Use --hotkey <combo> or edit config file");
            return;
        }
        using var _hotkey = hotkey;
        hotkey.OnPushToTalkStart += () => Console.WriteLine("  🔴 Recording...");
        hotkey.OnPushToTalkStop += () => Console.WriteLine("  ⏹️  Stopped");

        Console.WriteLine($"  ⌨️  Hotkey: {config.Hotkey}");
        Console.WriteLine("  Ready! Hold hotkey to speak. Ctrl+C to quit.");
        Console.WriteLine();

        // Wait for exit
        var exitTcs = new TaskCompletionSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n👋 Shutting down...");
            exitTcs.TrySetResult();
        };

        hotkey.Start();
        await exitTcs.Task;

        Console.WriteLine("Goodbye!");
    }
}
