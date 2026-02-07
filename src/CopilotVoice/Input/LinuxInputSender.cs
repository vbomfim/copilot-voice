using CopilotVoice.Sessions;

namespace CopilotVoice.Input;

public class LinuxInputSender : IInputSender
{
    public bool IsSupported => OperatingSystem.IsLinux();

    public Task SendTextAsync(CopilotSession session, string text, bool pressEnter = true)
    {
        throw new NotImplementedException("Linux input sending is not yet implemented.");
    }
}
