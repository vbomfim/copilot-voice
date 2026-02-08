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

        // Set clipboard, activate terminal, paste, optionally press enter
        var enterLine = pressEnter
            ? "\ntell application \"System Events\" to keystroke return"
            : "";

        return $@"set the clipboard to ""{escaped}""
tell application ""{terminalApp}"" to activate
delay 0.1
tell application ""System Events"" to keystroke ""v"" using command down{enterLine}";
    }
}
