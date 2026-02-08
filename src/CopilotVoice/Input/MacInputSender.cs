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
        // Use System Events keystroke — types text into the frontmost app's
        // focused text field without executing it as a shell command.
        // This is safer than "do script" which runs text as a command.
        if (pressEnter)
        {
            return $"tell application \"System Events\" to keystroke \"{escapedText}\" & return";
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
