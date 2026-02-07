using CopilotVoice.Sessions;

namespace CopilotVoice.Input;

public class WindowsInputSender : IInputSender
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public Task SendTextAsync(CopilotSession session, string text, bool pressEnter = true)
    {
        throw new NotImplementedException("Windows input sending is not yet implemented.");
    }
}
