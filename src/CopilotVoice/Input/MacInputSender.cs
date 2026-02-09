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

        // Set clipboard via pbcopy (no trailing newline — bracketed paste treats it as literal)
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

        // Activate the target app
        await ActivateAppAsync(session.TerminalApp ?? "Terminal");
        await Task.Delay(300);

        // Cmd+V via CGEvent
        SendKeyCombo(kVK_V, kCGEventFlagMaskCommand);

        if (pressEnter)
        {
            // Scale delay with text length — longer text needs more time to paste
            var pasteDelay = Math.Max(500, Math.Min(2000, text.Length * 5));
            await Task.Delay(pasteDelay);
            SendKeyWithDelay(kVK_Return);
        }
    }

    private static async Task ActivateAppAsync(string appName)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName = "osascript",
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        proc.Start();
        await proc.StandardInput.WriteAsync($"tell application \"{appName}\" to activate");
        proc.StandardInput.Close();
        await proc.WaitForExitAsync();
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

    private static void SendKeyWithDelay(ushort keycode)
    {
        var down = CGEventCreateKeyboardEvent(IntPtr.Zero, keycode, true);
        var up = CGEventCreateKeyboardEvent(IntPtr.Zero, keycode, false);
        CGEventPost(0, down);
        Thread.Sleep(50); // realistic key press duration
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
