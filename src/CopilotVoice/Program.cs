using Avalonia;
using CopilotVoice.Config;

namespace CopilotVoice;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var cliArgs = CliArgs.Parse(args);
        if (cliArgs.ShowHelp) { CliArgs.PrintHelp(); return; }

        // One-shot: list sessions
        if (cliArgs.ListSessions)
        {
            var detector = new Sessions.SessionDetector();
            var sessions = detector.DetectSessions();
            if (sessions.Count == 0) { Console.WriteLine("No active Copilot CLI sessions found."); return; }
            foreach (var s in sessions)
                Console.WriteLine($"  {s.TerminalApp} — {s.Label} (PID: {s.ProcessId})");
            return;
        }

        // Apply CLI overrides via environment (AppServices reads them)
        if (cliArgs.Key != null) Environment.SetEnvironmentVariable("AZURE_SPEECH_KEY", cliArgs.Key);
        if (cliArgs.Region != null) Environment.SetEnvironmentVariable("AZURE_SPEECH_REGION", cliArgs.Region);

        // Launch Avalonia GUI app
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
