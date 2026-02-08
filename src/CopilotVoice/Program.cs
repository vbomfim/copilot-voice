using Avalonia;
using CopilotVoice.Config;
using CopilotVoice.Mcp;

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

        // MCP server mode: stdio JSON-RPC (no GUI)
        if (cliArgs.McpMode)
        {
            RunMcpServerAsync().GetAwaiter().GetResult();
            return;
        }

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

    /// <summary>
    /// Run as MCP server over stdio. Copilot CLI launches this process
    /// and communicates via JSON-RPC on stdin/stdout.
    /// </summary>
    private static async Task RunMcpServerAsync()
    {
        Console.Error.WriteLine("[copilot-voice] Starting in MCP server mode (stdio)");

        await using var mcpServer = new McpServer();
        mcpServer.OnLog += msg => Console.Error.WriteLine($"[copilot-voice] {msg}");

        // Wire tool handlers
        McpToolHandler.OnSpeak = async (text, voice) =>
        {
            await AppServices.SayStaticAsync(text);
        };
        McpToolHandler.OnSetAvatar = expr =>
        {
            Console.Error.WriteLine($"[copilot-voice] Avatar: {expr}");
        };
        McpToolHandler.OnNotify = async (message, speak) =>
        {
            Console.Error.WriteLine($"[copilot-voice] Notify: {message}");
            if (speak) await AppServices.SayStaticAsync(message);
        };

        // Connect stdin/stdout as a client
        var reader = new StreamReader(Console.OpenStandardInput());
        var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

        var client = await mcpServer.AddClientAsync(reader, writer);

        Console.Error.WriteLine("[copilot-voice] MCP server ready, waiting for client messages...");

        // Keep alive until stdin closes (client disconnects)
        var tcs = new TaskCompletionSource();
        client.OnDisconnected += _ => tcs.TrySetResult();
        await tcs.Task;

        Console.Error.WriteLine("[copilot-voice] MCP client disconnected, shutting down");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
