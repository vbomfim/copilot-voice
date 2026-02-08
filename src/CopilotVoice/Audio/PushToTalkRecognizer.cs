using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using CopilotVoice.Config;

namespace CopilotVoice.Audio;

public class PushToTalkRecognizer : IDisposable
{
    private SpeechRecognizer? _recognizer;
    private AudioConfig? _audioConfig;
    private readonly AppConfig _config;
    private readonly AzureAuthProvider _authProvider;
    private readonly List<string> _recognizedSegments = new();
    private readonly object _segmentsLock = new();
    private int _sessionId;
    private TaskCompletionSource? _sessionStoppedTcs;
    private bool _initialized;
    private bool _disposed;

    public event Action<string>? OnPartialResult;
    public event Action<string>? OnFinalResult;
    public event Action<string>? OnError;
    public event Action<string>? OnLog;

    public PushToTalkRecognizer(AppConfig config)
    {
        _config = config;
        _authProvider = new AzureAuthProvider();
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        var (key, region) = _authProvider.Resolve(_config);
        var speechConfig = SpeechConfig.FromSubscription(key, region);
        speechConfig.SpeechRecognitionLanguage = _config.Language;

        OnLog?.Invoke($"STT: connecting to {region}, lang={_config.Language}");

        _audioConfig = AudioConfig.FromDefaultMicrophoneInput();
        _recognizer = new SpeechRecognizer(speechConfig, _audioConfig);

        _recognizer.Recognizing += (s, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizingSpeech)
            {
                OnLog?.Invoke($"STT partial: {e.Result.Text}");
                OnPartialResult?.Invoke(e.Result.Text);
            }
        };

        _recognizer.Recognized += (s, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrEmpty(e.Result.Text))
            {
                OnLog?.Invoke($"STT final segment: {e.Result.Text}");
                lock (_segmentsLock)
                {
                    _recognizedSegments.Add(e.Result.Text);
                }
            }
            else if (e.Result.Reason == ResultReason.NoMatch)
            {
                OnLog?.Invoke("STT: no match (silence or noise)");
            }
        };

        _recognizer.SessionStarted += (s, e) =>
            OnLog?.Invoke("STT: session started");

        _recognizer.SessionStopped += (s, e) =>
        {
            OnLog?.Invoke("STT: session stopped");
            _sessionStoppedTcs?.TrySetResult();
        };

        _recognizer.Canceled += (s, e) =>
        {
            OnLog?.Invoke($"STT canceled: {e.Reason} — {e.ErrorCode}");
            if (e.Reason == CancellationReason.Error)
                OnError?.Invoke($"{e.ErrorCode}: {e.ErrorDetails}");
        };

        _initialized = true;
        OnLog?.Invoke("STT: initialized (reusable)");
    }

    public async Task StartRecordingAsync()
    {
        Interlocked.Increment(ref _sessionId);
        lock (_segmentsLock) { _recognizedSegments.Clear(); }
        _sessionStoppedTcs = new TaskCompletionSource();
        EnsureInitialized();

        await _recognizer!.StartContinuousRecognitionAsync();
        OnLog?.Invoke("STT: recording started");
    }

    public async Task<string> StopRecordingAndTranscribeAsync()
    {
        if (_recognizer == null) return string.Empty;

        OnLog?.Invoke("STT: stopping recognition...");

        try
        {
            var stopTask = _recognizer.StopContinuousRecognitionAsync();
            if (await Task.WhenAny(stopTask, Task.Delay(5000)) != stopTask)
            {
                OnLog?.Invoke("STT: stop timed out after 5s");
                OnError?.Invoke("Recognition stop timed out");
            }
            else
            {
                OnLog?.Invoke("STT: recognition stopped");
            }
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"STT stop error: {ex.Message}");
        }

        // Wait for SessionStopped event — Azure delivers final segments before this fires
        if (_sessionStoppedTcs != null)
        {
            if (await Task.WhenAny(_sessionStoppedTcs.Task, Task.Delay(3000)) != _sessionStoppedTcs.Task)
                OnLog?.Invoke("STT: SessionStopped timed out after 3s");
        }

        string[] segments;
        lock (_segmentsLock)
        {
            segments = _recognizedSegments.ToArray();
            _recognizedSegments.Clear();
        }

        var result = string.Join(" ", segments);
        OnLog?.Invoke($"STT result: \"{result}\" ({segments.Length} segments)");
        if (!string.IsNullOrEmpty(result))
            OnFinalResult?.Invoke(result);
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _recognizer?.Dispose();
        _audioConfig?.Dispose();
        GC.SuppressFinalize(this);
    }
}
