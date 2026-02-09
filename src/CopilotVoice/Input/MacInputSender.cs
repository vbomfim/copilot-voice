using System.Diagnostics;
using System.Runtime.InteropServices;
using CopilotVoice.Sessions;

namespace CopilotVoice.Input;

public class MacInputSender : IInputSender
{
    public bool IsSupported => OperatingSystem.IsMacOS();

    // CGEvent P/Invoke for direct keystroke sending (no osascript needed)
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort keycode, bool keyDown);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventSetFlags(IntPtr ev, ulong flags);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventPost(int tap, IntPtr ev);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    private const ushort kVK_V = 9;
    private const ushort kVK_Return = 36;
    private const ulong kCGEventFlagMaskCommand = 1UL << 20;

    public async Task SendTextAsync(CopilotSession session, string text, bool pressEnter = true)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(text);

        Console.Error.WriteLine($"[MacInputSender] Pasting {text.Length} chars to {session.TerminalApp}: \"{text[..Math.Min(80, text.Length)]}\"");

        // Set clipboard via pbcopy
        using (var proc = new Process())
        {
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "pbcopy",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            proc.Start();
            await proc.StandardInput.WriteAsync(text);
            proc.StandardInput.Close();
            await proc.WaitForExitAsync();
        }

        // Activate the target app via stdin (avoids shell quoting issues)
        using (var proc = new Process())
        {
            var app = session.TerminalApp ?? "Terminal";
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "osascript",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            proc.Start();
            await proc.StandardInput.WriteAsync($"tell application \"{app}\" to activate");
            proc.StandardInput.Close();
            await proc.WaitForExitAsync();
        }

        await Task.Delay(150);

        // Cmd+V via CGEvent (uses CopilotVoice's own Accessibility permission)
        SendKeyCombo(kVK_V, kCGEventFlagMaskCommand);

        if (pressEnter)
        {
            // Scale delay with text length — longer text needs more time to paste
            var pasteDelay = Math.Max(500, Math.Min(2000, text.Length * 5));
            await Task.Delay(pasteDelay);
            SendKey(kVK_Return);
        }
    }

    private static void SendKey(ushort keycode)
    {
        var down = CGEventCreateKeyboardEvent(IntPtr.Zero, keycode, true);
        var up = CGEventCreateKeyboardEvent(IntPtr.Zero, keycode, false);
        CGEventPost(0, down);
        CGEventPost(0, up);
        CFRelease(down);
        CFRelease(up);
    }

    private static void SendKeyCombo(ushort keycode, ulong flags)
    {
        var down = CGEventCreateKeyboardEvent(IntPtr.Zero, keycode, true);
        CGEventSetFlags(down, flags);
        var up = CGEventCreateKeyboardEvent(IntPtr.Zero, keycode, false);
        CGEventPost(0, down);
        CGEventPost(0, up);
        CFRelease(down);
        CFRelease(up);
    }
}
