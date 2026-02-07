using CopilotVoice.Sessions;

namespace CopilotVoice.Input;

public interface IInputSender
{
    Task SendTextAsync(CopilotSession session, string text, bool pressEnter = true);
    bool IsSupported { get; }
}
