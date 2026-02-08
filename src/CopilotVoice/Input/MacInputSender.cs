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

        // Strategy: activate Terminal, then use clipboard paste
        // This avoids both "do script" (runs as command) and
        // "keystroke" (types into wrong window) issues.
        var script = BuildClipboardPasteScript(session, text, pressEnter);

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

    private static string BuildClipboardPasteScript(CopilotSession session, string text, bool pressEnter)
    {
        var app = session.TerminalApp ?? "Terminal";
        var escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"");

        var enterLine = pressEnter
            ? "\nkeystroke return"
            : "";

        // For apps with AppleScript support (Terminal.app, iTerm2),
        // find the copilot window. For others (Ghostty, Alacritty),
        // just activate the app and paste.
        return $@"-- Save current clipboard
set oldClip to the clipboard
set the clipboard to ""{escaped}""
tell application ""{app}"" to activate
delay 0.15
tell application ""System Events""
    keystroke ""v"" using command down{enterLine}
end tell
-- Restore clipboard after brief delay
delay 0.1
set the clipboard to oldClip";
    }
}
