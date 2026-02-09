using Avalonia;
using CopilotVoice.Config;
using CopilotVoice.Mcp;

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

        // One-shot: register this terminal as a session
        if (cliArgs.RegisterSession)
        {
            RegisterSessionAsync(cliArgs).GetAwaiter().GetResult();
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
            CrashLog("[AVALONIA] Starting desktop lifetime...");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            CrashLog("[AVALONIA] Desktop lifetime ended normally");
        }
        catch (Exception ex)
        {
            CrashLog($"[CRASH] Avalonia: {ex}");
        }
    }

    /// <summary>
    /// One-shot: register the calling terminal as a Copilot CLI session
    /// by POSTing to the running tray app's HTTP listener.
    /// </summary>
    private static async Task RegisterSessionAsync(CliArgs cliArgs)
    {
        var parentPid = Environment.ProcessId;
        var terminalApp = string.Empty;
        try
        {
            // Walk up the process tree past shells and intermediaries to find the terminal emulator
            var skipProcesses = new[] { "login", "zsh", "bash", "sh", "fish", "nu", "dotnet", "copilot", "gh", "node" };
            var currentPid = Environment.ProcessId;

            while (true)
            {
                var ppidStr = Sessions.SessionDetector.RunCommandStatic("ps", $"-p {currentPid} -o ppid=").Trim();
                if (!int.TryParse(ppidStr, out var ppid) || ppid <= 1)
                    break;

                var comm = Sessions.SessionDetector.RunCommandStatic("ps", $"-p {ppid} -o comm=").Trim();
                var commName = Path.GetFileName(comm);

                if (skipProcesses.Any(s => commName.Equals(s, StringComparison.OrdinalIgnoreCase))
                    || commName.StartsWith("-")) // login shells like -/bin/zsh
                {
                    parentPid = ppid;
                    currentPid = ppid;
                    continue;
                }

                // Found a non-shell, non-intermediary parent — likely the terminal emulator
                parentPid = ppid;
                terminalApp = commName;
                break;
            }
        }
        catch { /* use own PID as fallback */ }

        // Try to get the window title of our terminal app (not just frontmost)
        var windowTitle = string.Empty;
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)
            && !string.IsNullOrEmpty(terminalApp))
        {
            // Map process name to macOS app name (used for osascript and stored in session)
            terminalApp = terminalApp switch
            {
                "ghostty" => "Ghostty",
                "iTerm2" or "iTermServer-main" => "iTerm2",
                "Terminal" => "Terminal",
                "kitty" => "kitty",
                "Alacritty" or "alacritty" => "Alacritty",
                "wezterm-gui" => "WezTerm",
                "Hyper" => "Hyper",
                var x when x.Contains("Code - Insiders") => "Visual Studio Code - Insiders",
                var x when x.Contains("Code") => "Visual Studio Code",
                _ => terminalApp
            };

            try
            {
                var title = Sessions.SessionDetector.RunCommandStatic("osascript",
                    $"-e \"tell application \\\"{terminalApp}\\\" to get name of front window\"").Trim();
                if (!string.IsNullOrEmpty(title))
                    windowTitle = title;
            }
            catch { /* ignore */ }
        }

        var label = cliArgs.RegisterLabel
            ?? (string.IsNullOrEmpty(windowTitle) ? string.Empty : windowTitle);

        var request = new Messaging.RegisterRequest
        {
            Pid = parentPid,
            WorkingDirectory = Environment.CurrentDirectory,
            Label = label,
            TerminalApp = terminalApp
        };

        var json = System.Text.Json.JsonSerializer.Serialize(request);
        using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        try
        {
            var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("http://localhost:7701/register", content);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
                Console.WriteLine($"✅ Session registered: PID {parentPid}, cwd: {Environment.CurrentDirectory}");
            else
                Console.Error.WriteLine($"❌ Registration failed: {body}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ Cannot connect to copilot-voice (is it running?): {ex.Message}");
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
