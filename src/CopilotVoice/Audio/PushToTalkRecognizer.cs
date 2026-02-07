using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using CopilotVoice.Config;

namespace CopilotVoice.Audio;

public class PushToTalkRecognizer : IDisposable
{
    private SpeechRecognizer? _recognizer;
    private readonly AppConfig _config;
    private readonly AzureAuthProvider _authProvider;
    private readonly List<string> _recognizedSegments = new();
    private bool _disposed;

    public event Action<string>? OnPartialResult;
    public event Action<string>? OnError;

    public PushToTalkRecognizer(AppConfig config)
    {
        _config = config;
        _authProvider = new AzureAuthProvider();
    }

    public async Task StartRecordingAsync()
    {
        _recognizedSegments.Clear();

        var (key, region) = _authProvider.Resolve(_config);
        var speechConfig = SpeechConfig.FromSubscription(key, region);
        speechConfig.SpeechRecognitionLanguage = _config.Language;

        var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
        _recognizer = new SpeechRecognizer(speechConfig, audioConfig);

        _recognizer.Recognizing += (s, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizingSpeech)
                OnPartialResult?.Invoke(e.Result.Text);
        };

        _recognizer.Recognized += (s, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrEmpty(e.Result.Text))
                _recognizedSegments.Add(e.Result.Text);
        };

        _recognizer.Canceled += (s, e) =>
        {
            if (e.Reason == CancellationReason.Error)
                OnError?.Invoke($"{e.ErrorCode}: {e.ErrorDetails}");
        };

        await _recognizer.StartContinuousRecognitionAsync();
    }

    public async Task<string> StopRecordingAndTranscribeAsync()
    {
        if (_recognizer == null) return string.Empty;

        await _recognizer.StopContinuousRecognitionAsync();
        _recognizer.Dispose();
        _recognizer = null;

        return string.Join(" ", _recognizedSegments);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _recognizer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
