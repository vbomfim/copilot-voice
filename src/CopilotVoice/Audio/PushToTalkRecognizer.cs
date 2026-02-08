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

        // Use auto language detection for multilingual speakers
        var autoDetectConfig = AutoDetectSourceLanguageConfig.FromLanguages(
            new[] { _config.Language, "pt-BR", "es-ES" });

        // Request detailed results for better accuracy
        speechConfig.OutputFormat = OutputFormat.Detailed;
        // Allow profanity (raw mode gives more accurate transcription)
        speechConfig.SetProfanity(ProfanityOption.Raw);
        // Enable dictation mode for natural speech (pauses, punctuation)
        speechConfig.EnableDictation();

        OnLog?.Invoke($"STT: connecting to {region}, lang={_config.Language} (auto-detect enabled)");

        _audioConfig = AudioConfig.FromDefaultMicrophoneInput();
        _recognizer = new SpeechRecognizer(speechConfig, autoDetectConfig, _audioConfig);

        // Add phrase hints for common terms to improve accuracy
        var phraseList = PhraseListGrammar.FromRecognizer(_recognizer);
        // Dev tools & platforms
        phraseList.AddPhrase("Copilot");
        phraseList.AddPhrase("Copilot Voice");
        phraseList.AddPhrase("CLI");
        phraseList.AddPhrase("MCP");
        phraseList.AddPhrase("GitHub");
        phraseList.AddPhrase("Ghostty");
        phraseList.AddPhrase("SSE");
        phraseList.AddPhrase("async");
        phraseList.AddPhrase("await");
        phraseList.AddPhrase("dotnet");
        phraseList.AddPhrase("npm");
        phraseList.AddPhrase("TypeScript");
        phraseList.AddPhrase("JavaScript");
        phraseList.AddPhrase("API");
        phraseList.AddPhrase("JSON");
        phraseList.AddPhrase("codebase");
        phraseList.AddPhrase("repository");
        phraseList.AddPhrase("commit");
        phraseList.AddPhrase("push");
        phraseList.AddPhrase("pull request");
        phraseList.AddPhrase("terminal");
        phraseList.AddPhrase("tray app");
        phraseList.AddPhrase("hotkey");
        // UI & app terms
        phraseList.AddPhrase("floating window");
        phraseList.AddPhrase("avatar");
        phraseList.AddPhrase("tray icon");
        phraseList.AddPhrase("Ctrl+Space");
        phraseList.AddPhrase("push to talk");
        phraseList.AddPhrase("transcription");
        phraseList.AddPhrase("speech to text");
        phraseList.AddPhrase("text to speech");
        // C# / .NET
        phraseList.AddPhrase("Avalonia");
        phraseList.AddPhrase("NuGet");
        phraseList.AddPhrase("SharpHook");
        phraseList.AddPhrase("CGEvent");
        phraseList.AddPhrase("P/Invoke");
        phraseList.AddPhrase("Azure");
        phraseList.AddPhrase("XAML");
        // Common commands
        phraseList.AddPhrase("build it");
        phraseList.AddPhrase("run it");
        phraseList.AddPhrase("fix it");
        phraseList.AddPhrase("deploy");
        phraseList.AddPhrase("restart");

        // Load custom phrases from config if available
        if (_config.CustomPhrases != null)
        {
            foreach (var phrase in _config.CustomPhrases)
                phraseList.AddPhrase(phrase);
        }

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

    /// <summary>Reset the recognizer after an error to avoid native SDK abort.</summary>
    private void ResetRecognizer()
    {
        _isRecording = false;
        _initialized = false;
        try { _recognizer?.Dispose(); } catch { }
        try { _audioConfig?.Dispose(); } catch { }
        _recognizer = null;
        _audioConfig = null;
        OnLog?.Invoke("STT: recognizer reset after error");
    }

    private bool _isRecording;

    public async Task StartRecordingAsync()
    {
        if (_isRecording)
        {
            OnLog?.Invoke("STT: already recording, skipping start");
            return;
        }

        Interlocked.Increment(ref _sessionId);
        lock (_segmentsLock) { _recognizedSegments.Clear(); }
        _sessionStoppedTcs = new TaskCompletionSource();
        EnsureInitialized();

        try
        {
            await _recognizer!.StartContinuousRecognitionAsync();
            _isRecording = true;
            OnLog?.Invoke("STT: recording started");
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"STT: start failed: {ex.Message}");
            ResetRecognizer();
            throw;
        }
    }

    public async Task<string> StopRecordingAndTranscribeAsync()
    {
        if (_recognizer == null || !_isRecording) return string.Empty;
        _isRecording = false;

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
