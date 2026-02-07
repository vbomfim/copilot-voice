using System.Runtime.InteropServices;

namespace CopilotVoice.Input;

public static class InputSenderFactory
{
    public static IInputSender Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return new MacInputSender();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return new LinuxInputSender();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return new WindowsInputSender();
        throw new PlatformNotSupportedException();
    }
}
