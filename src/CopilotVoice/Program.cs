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
    /// Run as MCP relay over stdio. Copilot CLI launches this process
    /// and communicates via JSON-RPC on stdin/stdout.
    /// We relay to the tray app's MCP TCP server on localhost:7702.
    /// </summary>
    private static async Task RunMcpServerAsync()
    {
        Console.Error.WriteLine("[copilot-voice] Starting in MCP relay mode (stdio → TCP:7702)");

        // Try to connect to the tray app's TCP MCP server
        using var tcpClient = new System.Net.Sockets.TcpClient();
        try
        {
            await tcpClient.ConnectAsync(System.Net.IPAddress.Loopback, 7702);
            Console.Error.WriteLine("[copilot-voice] Connected to tray app MCP server");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[copilot-voice] Cannot connect to tray app on port 7702: {ex.Message}");
            Console.Error.WriteLine("[copilot-voice] Falling back to standalone MCP server");
            await RunStandaloneMcpServerAsync();
            return;
        }

        var tcpStream = tcpClient.GetStream();
        var stdinReader = new StreamReader(Console.OpenStandardInput());
        var stdoutWriter = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var tcpReader = new StreamReader(tcpStream);
        var tcpWriter = new StreamWriter(tcpStream) { AutoFlush = true };

        using var cts = new CancellationTokenSource();

        // Relay stdin → TCP
        var stdinToTcp = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var line = await stdinReader.ReadLineAsync(cts.Token);
                    if (line == null) break;
                    await tcpWriter.WriteLineAsync(line);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Console.Error.WriteLine($"[relay] stdin→tcp error: {ex.Message}"); }
            finally { cts.Cancel(); }
        }, cts.Token);

        // Relay TCP → stdout
        var tcpToStdout = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var line = await tcpReader.ReadLineAsync(cts.Token);
                    if (line == null) break;
                    await stdoutWriter.WriteLineAsync(line);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Console.Error.WriteLine($"[relay] tcp→stdout error: {ex.Message}"); }
            finally { cts.Cancel(); }
        }, cts.Token);

        Console.Error.WriteLine("[copilot-voice] MCP relay active");
        await Task.WhenAny(stdinToTcp, tcpToStdout);
        cts.Cancel();
        Console.Error.WriteLine("[copilot-voice] MCP relay shutting down");
    }

    /// <summary>
    /// Standalone MCP server (fallback when tray app is not running).
    /// </summary>
    private static async Task RunStandaloneMcpServerAsync()
    {
        await using var mcpServer = new McpServer();
        mcpServer.OnLog += msg => Console.Error.WriteLine($"[copilot-voice] {msg}");

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

        var reader = new StreamReader(Console.OpenStandardInput());
        var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

        var client = await mcpServer.AddClientAsync(reader, writer);

        Console.Error.WriteLine("[copilot-voice] Standalone MCP server ready");

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
