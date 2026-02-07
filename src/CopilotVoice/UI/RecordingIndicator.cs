namespace CopilotVoice.UI;

public class RecordingIndicator
{
    public void Show(string? partialText = null)
    {
        Console.WriteLine("  🔴 Listening...");
        if (partialText != null) Console.WriteLine($"  \"{partialText}\"");
    }

    public void UpdatePartialText(string text) { Console.Write($"\r  \"{text}\"    "); }

    public void ShowFinalAndHide(string text, int displayMs = 1500)
    {
        Console.WriteLine($"\n  ✅ Sent: \"{text}\"");
    }

    public void Hide() { }
}
