using System.Diagnostics;
using CopilotVoice.Sessions;

namespace CopilotVoice.Input;

public class MacInputSender : IInputSender
{
    public bool IsSupported => OperatingSystem.IsMacOS();

    public async Task SendTextAsync(CopilotSession session, string text, bool pressEnter = true)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(text);

        var escaped = EscapeForAppleScript(text);
        var script = BuildAppleScript(session, escaped, pressEnter);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "osascript",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        process.Start();
        await process.StandardInput.WriteLineAsync(script);
        process.StandardInput.Close();

        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"osascript failed (exit {process.ExitCode}): {stderr}");
    }

    private static string BuildAppleScript(CopilotSession session, string escapedText, bool pressEnter)
    {
        var app = session.TerminalApp?.ToLowerInvariant() ?? string.Empty;

        if (app.Contains("iterm"))
        {
            var enterClause = pressEnter ? " & \"\\n\"" : string.Empty;
            return $"tell application \"iTerm2\" to tell current session of current window to write text \"{escapedText}\"{enterClause}";
        }

        // Default: Terminal.app
        if (pressEnter)
        {
            return $"tell application \"Terminal\" to do script \"{escapedText}\" in front window";
        }

        return $"tell application \"System Events\" to keystroke \"{escapedText}\"";
    }

    private static string EscapeForAppleScript(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }
}
