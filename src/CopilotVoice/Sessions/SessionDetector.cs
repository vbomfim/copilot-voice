using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CopilotVoice.Sessions;

public class SessionDetector
{
    private List<CopilotSession> _sessions = new();

    public List<CopilotSession> DetectSessions()
    {
        _sessions = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? DetectMacSessions()
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? DetectLinuxSessions()
                : DetectWindowsSessions();
        return _sessions;
    }

    public void Refresh() => DetectSessions();

    public List<CopilotSession> GetCachedSessions() => _sessions;

    public CopilotSession? GetFocusedSession()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return null;

        try
        {
            var frontApp = RunCommand("osascript",
                "-e \"tell application \\\"System Events\\\" to get name of first application process whose frontmost is true\"")
                .Trim();

            var terminalApps = new[] { "Terminal", "iTerm2", "Alacritty", "kitty", "WezTerm", "Hyper", "Ghostty" };
            if (!terminalApps.Any(app => frontApp.Contains(app, StringComparison.OrdinalIgnoreCase)))
                return null;

            // Refresh sessions and find one matching the frontmost terminal
            if (_sessions.Count == 0)
                DetectSessions();

            foreach (var session in _sessions)
                session.IsFocused = false;

            var focused = _sessions.FirstOrDefault(s =>
                s.TerminalApp.Contains(frontApp, StringComparison.OrdinalIgnoreCase));

            if (focused != null)
                focused.IsFocused = true;

            return focused;
        }
        catch
        {
            return null;
        }
    }

    private List<CopilotSession> DetectMacSessions()
    {
        var sessions = new List<CopilotSession>();

        try
        {
            // Find copilot-related processes
            var psOutput = RunCommand("ps", "aux");
            var copilotProcesses = psOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("copilot", StringComparison.OrdinalIgnoreCase)
                            && !line.Contains("copilot-voice", StringComparison.OrdinalIgnoreCase)
                            && !line.Contains("grep", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var proc in copilotProcesses)
            {
                var parts = proc.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !int.TryParse(parts[1], out var pid))
                    continue;

                var cwd = GetProcessCwd(pid);
                var terminal = GetTerminalForProcess(pid);

                sessions.Add(new CopilotSession
                {
                    Id = $"session-{pid}",
                    ProcessId = pid,
                    WorkingDirectory = cwd ?? "unknown",
                    TerminalApp = terminal ?? "Terminal",
                    TerminalTitle = $"Copilot CLI (PID {pid})"
                });
            }

            // Also try to detect via terminal windows
            if (sessions.Count == 0)
            {
                sessions.AddRange(DetectFromTerminalApp());
                sessions.AddRange(DetectFromITerm2());
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error detecting sessions: {ex.Message}");
        }

        return sessions;
    }

    private List<CopilotSession> DetectFromTerminalApp()
    {
        var sessions = new List<CopilotSession>();
        try
        {
            var script = @"
                tell application ""Terminal""
                    set windowList to every window
                    set result to """"
                    repeat with w in windowList
                        try
                            set tabList to every tab of w
                            repeat with t in tabList
                                set procList to processes of t
                                set tTitle to name of w as text
                                repeat with p in procList
                                    if p contains ""copilot"" then
                                        set result to result & tTitle & ""|"" & p & linefeed
                                    end if
                                end repeat
                            end repeat
                        end try
                    end repeat
                    return result
                end tell";
            var output = RunCommand("osascript", $"-e '{script}'");
            // Parse output into sessions
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|');
                if (parts.Length >= 2)
                {
                    sessions.Add(new CopilotSession
                    {
                        Id = $"terminal-{sessions.Count}",
                        TerminalApp = "Terminal",
                        TerminalTitle = parts[0].Trim(),
                        WorkingDirectory = parts[0].Trim()
                    });
                }
            }
        }
        catch { /* Terminal.app not available */ }
        return sessions;
    }

    private List<CopilotSession> DetectFromITerm2()
    {
        var sessions = new List<CopilotSession>();
        try
        {
            var script = @"
                tell application ""iTerm2""
                    set result to """"
                    repeat with w in windows
                        repeat with t in tabs of w
                            repeat with s in sessions of t
                                set sName to name of s
                                if sName contains ""copilot"" then
                                    set result to result & sName & linefeed
                                end if
                            end repeat
                        end repeat
                    end repeat
                    return result
                end tell";
            var output = RunCommand("osascript", $"-e '{script}'");
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                sessions.Add(new CopilotSession
                {
                    Id = $"iterm-{sessions.Count}",
                    TerminalApp = "iTerm2",
                    TerminalTitle = line.Trim(),
                    WorkingDirectory = line.Trim()
                });
            }
        }
        catch { /* iTerm2 not available */ }
        return sessions;
    }

    private string? GetProcessCwd(int pid)
    {
        try
        {
            var output = RunCommand("lsof", $"-p {pid} -Fn");
            var cwdLine = output.Split('\n')
                .FirstOrDefault(l => l.StartsWith("n") && l.Contains("/"));
            return cwdLine?[1..]; // Remove 'n' prefix
        }
        catch { return null; }
    }

    private string? GetTerminalForProcess(int pid)
    {
        try
        {
            // Walk up the process tree to find the terminal emulator
            var output = RunCommand("ps", $"-p {pid} -o ppid=").Trim();
            if (int.TryParse(output, out var ppid))
            {
                var parentCmd = RunCommand("ps", $"-p {ppid} -o comm=").Trim();
                // Keep walking up if parent is login/zsh/bash
                var shells = new[] { "login", "zsh", "bash", "sh", "fish" };
                while (shells.Any(s => parentCmd.EndsWith(s)))
                {
                    output = RunCommand("ps", $"-p {ppid} -o ppid=").Trim();
                    if (!int.TryParse(output, out ppid)) break;
                    parentCmd = RunCommand("ps", $"-p {ppid} -o comm=").Trim();
                }
                // Map known terminal emulator process names to app names
                if (parentCmd.Contains("ghostty", StringComparison.OrdinalIgnoreCase)) return "Ghostty";
                if (parentCmd.Contains("iterm", StringComparison.OrdinalIgnoreCase)) return "iTerm2";
                if (parentCmd.Contains("alacritty", StringComparison.OrdinalIgnoreCase)) return "Alacritty";
                if (parentCmd.Contains("kitty", StringComparison.OrdinalIgnoreCase)) return "kitty";
                if (parentCmd.Contains("wezterm", StringComparison.OrdinalIgnoreCase)) return "WezTerm";
                if (parentCmd.Contains("Terminal", StringComparison.OrdinalIgnoreCase)) return "Terminal";
                return parentCmd; // return whatever it is
            }
            return "Terminal";
        }
        catch { return "Terminal"; }
    }

    private List<CopilotSession> DetectLinuxSessions()
    {
        // TODO: Implement Linux detection via /proc
        return new List<CopilotSession>();
    }

    private List<CopilotSession> DetectWindowsSessions()
    {
        // TODO: Implement Windows detection via Process API
        return new List<CopilotSession>();
    }

    private static string RunCommand(string command, string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);
        return output;
    }
}
