using Avalonia;
using CopilotVoice.Config;

namespace CopilotVoice;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Global exception handlers to prevent silent crashes
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var msg = $"[CRASH] Unhandled: {e.ExceptionObject}";
            Console.Error.WriteLine(msg);
            File.AppendAllText("/tmp/copilot-voice-crash.log", $"{DateTime.Now}: {msg}\n");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            var msg = $"[CRASH] Unobserved task: {e.Exception}";
            Console.Error.WriteLine(msg);
            File.AppendAllText("/tmp/copilot-voice-crash.log", $"{DateTime.Now}: {msg}\n");
            e.SetObserved();
        };

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
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CRASH] {ex}");
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
