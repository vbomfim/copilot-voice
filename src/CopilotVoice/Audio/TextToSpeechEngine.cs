using System.Security;
using System.Text.RegularExpressions;
using CopilotVoice.Config;
using Microsoft.CognitiveServices.Speech;

namespace CopilotVoice.Audio;

public partial class TextToSpeechEngine : IDisposable
{
    private readonly AppConfig _config;
    private readonly SpeechSynthesizer _synthesizer;
    private bool _disposed;

    public event Action? OnSpeechStarted;
    public event Action? OnSpeechFinished;
    public event Action<string>? OnError;

    public TextToSpeechEngine(AppConfig config)
    {
        _config = config;

        var auth = new AzureAuthProvider();
        var (key, region) = auth.Resolve(config);

        var speechConfig = SpeechConfig.FromSubscription(key, region);
        speechConfig.SpeechSynthesisVoiceName = config.VoiceName;

        _synthesizer = new SpeechSynthesizer(speechConfig);
        _synthesizer.SynthesisStarted += (_, _) => OnSpeechStarted?.Invoke();
        _synthesizer.SynthesisCompleted += (_, _) => OnSpeechFinished?.Invoke();
        _synthesizer.SynthesisCanceled += (_, e) =>
        {
            var details = SpeechSynthesisCancellationDetails.FromResult(e.Result);
            OnError?.Invoke(details.ErrorDetails);
        };
    }

    /// <summary>
    /// Speaks the given text aloud. Returns audio duration in seconds.
    /// </summary>
    public async Task<double> SpeakAsync(string text)
    {
        var ssml = BuildSsml(text, _config.VoiceName, _config.VoiceSpeed);
        var result = await _synthesizer.SpeakSsmlAsync(ssml);

        if (result.Reason == ResultReason.Canceled)
        {
            var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
            OnError?.Invoke(cancellation.ErrorDetails);
            return 0;
        }

        return result.AudioDuration.TotalSeconds;
    }

    /// <summary>
    /// Splits long text into chunks and speaks each sequentially.
    /// Returns total duration in seconds.
    /// </summary>
    public async Task<double> SpeakLongTextAsync(string text)
    {
        var chunks = SplitText(text);
        var totalDuration = 0.0;

        foreach (var chunk in chunks)
        {
            var duration = await SpeakAsync(chunk);
            totalDuration += duration;
        }

        return totalDuration;
    }

    /// <summary>
    /// Stops any current speech synthesis.
    /// </summary>
    public void Stop()
    {
        _synthesizer.StopSpeakingAsync().Wait();
    }

    /// <summary>
    /// Builds SSML with voice and speed (prosody rate) control.
    /// </summary>
    internal static string BuildSsml(string text, string voiceName, double speed)
    {
        var ratePercent = (int)((speed - 1.0) * 100);
        var rateStr = ratePercent >= 0 ? $"+{ratePercent}%" : $"{ratePercent}%";

        return $"""
            <speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="en-US">
              <voice name="{voiceName}">
                <prosody rate="{rateStr}">
                  {SecurityElement.Escape(text)}
                </prosody>
              </voice>
            </speak>
            """;
    }

    /// <summary>
    /// Splits text into chunks of at most 3 sentences each.
    /// </summary>
    internal static List<string> SplitText(string text, int maxSentencesPerChunk = 3)
    {
        var sentences = SentencePattern().Split(text)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        var chunks = new List<string>();
        for (var i = 0; i < sentences.Count; i += maxSentencesPerChunk)
        {
            var chunk = string.Join(" ", sentences.Skip(i).Take(maxSentencesPerChunk));
            chunks.Add(chunk.Trim());
        }

        if (chunks.Count == 0)
            chunks.Add(text);

        return chunks;
    }

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentencePattern();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _synthesizer.Dispose();
        GC.SuppressFinalize(this);
    }
}
