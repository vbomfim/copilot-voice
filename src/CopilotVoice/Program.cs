using Avalonia;

namespace CopilotVoice;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        const string crashLog = "/tmp/copilot-voice-crash.log";
        void CrashLog(string msg) { Console.Error.WriteLine(msg); try { File.AppendAllText(crashLog, $"{DateTime.Now}: {msg}\n"); } catch { } }

        CrashLog($"[START] PID={Environment.ProcessId} args=[{string.Join(", ", args)}]");

        // Global exception handlers to prevent silent crashes
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog($"[CRASH] Unhandled: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog($"[CRASH] Unobserved task: {e.Exception}");
            e.SetObserved();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            CrashLog($"[EXIT] Process exiting, stack:\n{Environment.StackTrace}");

        Console.CancelKeyPress += (_, e) =>
            CrashLog("[SIGNAL] Ctrl+C / SIGINT received");

        System.Runtime.Loader.AssemblyLoadContext.Default.Unloading += _ =>
            CrashLog("[UNLOAD] Assembly unloading (SIGTERM?)");

        var cliArgs = CliArgs.Parse(args);
        if (cliArgs.ShowHelp) { CliArgs.PrintHelp(); return; }

        // Apply CLI overrides via environment (AppServices reads them)
        if (cliArgs.Key != null) Environment.SetEnvironmentVariable("AZURE_SPEECH_KEY", cliArgs.Key);
        if (cliArgs.Region != null) Environment.SetEnvironmentVariable("AZURE_SPEECH_REGION", cliArgs.Region);

        // Launch Avalonia GUI app
        try
        {
            CrashLog("[AVALONIA] Starting desktop lifetime...");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            CrashLog("[AVALONIA] Desktop lifetime ended normally");
        }
        catch (Exception ex)
        {
            CrashLog($"[CRASH] Avalonia: {ex}");
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
