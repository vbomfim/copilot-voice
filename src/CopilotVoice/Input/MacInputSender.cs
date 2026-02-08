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
        var app = session.TerminalApp?.ToLowerInvariant() ?? "terminal";
        var terminalApp = app.Contains("iterm") ? "iTerm2" : "Terminal";
        var escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // Find the window whose name contains the session's title or "copilot",
        // set clipboard, bring that window to front, paste, optionally enter
        var enterLine = pressEnter
            ? "\nkeystroke return"
            : "";

        return $@"set the clipboard to ""{escaped}""
tell application ""{terminalApp}""
    activate
    set targetWindow to missing value
    repeat with w in windows
        set wName to name of w
        if wName contains ""copilot"" then
            set targetWindow to w
            exit repeat
        end if
    end repeat
    if targetWindow is not missing value then
        set index of targetWindow to 1
    end if
end tell
delay 0.15
tell application ""System Events""
    keystroke ""v"" using command down{enterLine}
end tell";
    }
}
