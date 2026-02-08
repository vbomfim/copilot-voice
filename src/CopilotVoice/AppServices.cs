using CopilotVoice.Audio;
using CopilotVoice.Config;
using CopilotVoice.Input;
using CopilotVoice.Messaging;
using CopilotVoice.Sessions;
using CopilotVoice.UI.Avatar;

namespace CopilotVoice;

/// <summary>
/// Centralizes all backend services and exposes events for the Avalonia UI.
/// </summary>
public sealed class AppServices : IDisposable
{
    public AppConfig Config { get; }
    public AvatarState AvatarState { get; } = new();
    public AvatarAnimator Animator { get; } = new();

    private readonly ConfigManager _configManager;
    private readonly SessionDetector _sessionDetector;
    private readonly SessionManager _sessionManager;
    private PushToTalkRecognizer? _stt;
    private TextToSpeechEngine? _tts;
    private IInputSender? _inputSender;
    private MessageListener? _messageListener;
    private Hotkey.HotkeyListener? _hotkey;
    private bool _isRecording;
    private bool _isBusy; // prevents re-entrant hotkey handling
    private bool _disposed;

    // UI events
    public event Action<string>? OnStateChanged;
    public event Action<string?, string?>? OnSpeechBubble;
    public event Action<string?>? OnTargetSession;
    public event Action<string?, TimeSpan>? OnTimerTick;
    public event Action<List<CopilotSession>>? OnSessionsRefreshed;
    public event Action<string>? OnLog;

    public AppServices()
    {
        _configManager = new ConfigManager();
        Config = _configManager.LoadOrCreate();

        // Apply env var overrides
        var envKey = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
        var envRegion = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION");
        if (!string.IsNullOrEmpty(envKey))
        {
            Config.AzureSpeechKey = envKey;
            Config.AuthMode = AuthMode.Env;
        }
        if (!string.IsNullOrEmpty(envRegion))
            Config.AzureSpeechRegion = envRegion;

        _sessionDetector = new SessionDetector();
        _sessionManager = new SessionManager(_sessionDetector);
    }

    public async Task StartAsync()
    {
        Log("Starting services...");

        // Auth check
        var auth = new AzureAuthProvider();
        try
        {
            var (_, region) = auth.Resolve(Config);
            Log($"Azure Speech: {region}");
        }
        catch (Exception ex)
        {
            Log($"Auth failed: {ex.Message}");
            OnStateChanged?.Invoke("Error");
            return;
        }

        // Sessions
        var sessions = _sessionDetector.DetectSessions();
        Log($"Found {sessions.Count} session(s)");
        OnSessionsRefreshed?.Invoke(sessions);

        _sessionManager.OnTargetChanged += s =>
        {
            OnTargetSession?.Invoke(s?.Label);
            Log($"Target: {s?.Label ?? "none"}");
        };
        _sessionManager.StartWatching();

        // Input sender
        try
        {
            _inputSender = InputSenderFactory.Create();
            Log($"Input sender: {_inputSender.GetType().Name}");
        }
        catch (Exception ex)
        {
            Log($"Input sender: {ex.Message}");
        }

        // TTS
        if (Config.EnableVoiceOutput)
        {
            try
            {
                _tts = new TextToSpeechEngine(Config);
                _tts.OnSpeechStarted += () =>
                {
                    Animator.RecordInteraction();
                    OnStateChanged?.Invoke("Speaking");
                };
                _tts.OnSpeechFinished += () => OnStateChanged?.Invoke("Ready");
                _tts.OnError += err => Log($"TTS error: {err}");
                Log($"TTS: {Config.VoiceName}");
            }
            catch (Exception ex)
            {
                Log($"TTS init failed: {ex.Message}");
            }
        }

        // STT
        _stt = new PushToTalkRecognizer(Config);
        _stt.OnPartialResult += text =>
        {
            Animator.RecordInteraction();
            OnSpeechBubble?.Invoke(text, null);
        };
        _stt.OnError += err => Log($"STT error: {err}");
        _stt.OnLog += msg => Log(msg);

        // Message listener
        try
        {
            _messageListener = new MessageListener();
            _messageListener.OnMessageReceived += async msg =>
            {
                try
                {
                    OnSpeechBubble?.Invoke(msg.Text, msg.SessionLabel);
                    await SayAsync(msg.Text);
                    OnSpeechBubble?.Invoke(null, null);
                }
                catch (Exception ex) { Log($"Message handler error: {ex.Message}"); }
            };
            _messageListener.Start();
            Log("Message listener: localhost:7701");
        }
        catch (Exception ex)
        {
            Log($"Message listener: {ex.Message}");
        }

        // Hotkey
        try
        {
            _hotkey = new Hotkey.HotkeyListener(Config.Hotkey);
            _hotkey.OnError += msg => Log($"Hotkey: {msg}");
            _hotkey.OnPushToTalkStart += OnHotkeyDown;
            _hotkey.OnPushToTalkStop += OnHotkeyUp;
            _hotkey.Start();
            Log($"Hotkey: {Config.Hotkey}");
        }
        catch (Exception ex)
        {
            Log($"Hotkey failed: {ex.Message}");
        }

        // Start avatar idle animation
        Animator.StartIdleLoop();
        OnStateChanged?.Invoke("Ready");
        Log("Ready!");
    }

    private async void OnHotkeyDown()
    {
        if (_isRecording || _isBusy || _stt == null) return;
        _isRecording = true;

        Log("Hotkey pressed — starting recording");
        OnStateChanged?.Invoke("Recording");
        Animator.RecordInteraction();

        try
        {
            await _stt.StartRecordingAsync();
        }
        catch (Exception ex)
        {
            Log($"Mic error: {ex.Message}");
            OnStateChanged?.Invoke("Error");
            _isRecording = false;
        }
    }

    private async void OnHotkeyUp()
    {
        if (!_isRecording || _stt == null) return;
        _isRecording = false;
        _isBusy = true;

        Log("Hotkey released — stopping recording");
        OnStateChanged?.Invoke("Transcribing");

        try
        {
            var text = await _stt.StopRecordingAndTranscribeAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                Log("No speech detected");
                OnStateChanged?.Invoke("Ready");
                return;
            }

            Log($"Transcribed: {text}");

            // Always copy to clipboard
            try
            {
                await CopyToClipboardAsync(text);
                Log("Copied to clipboard");
            }
            catch (Exception clipEx)
            {
                Log($"Clipboard failed: {clipEx.Message}");
            }

            // Send to target session
            var target = _sessionManager.GetTargetSession();
            if (target != null && _inputSender != null)
            {
                Log($"Sending to {target.Label} (PID:{target.ProcessId}, app:{target.TerminalApp})");
                await _inputSender.SendTextAsync(target, text, Config.AutoPressEnter);
                Log($"Sent to {target.Label}");
                OnSpeechBubble?.Invoke(text, target.Label);
            }
            else
            {
                Log("No target session — text is in clipboard (Cmd+V to paste)");
                OnSpeechBubble?.Invoke($"📋 {text}", "clipboard");
            }

            // Brief TTS confirmation via macOS say (Azure SDK hangs in Avalonia)
            _ = Task.Run(async () =>
            {
                try
                {
                    await SayAsync("Got it.");
                }
                catch (Exception ttsEx)
                {
                    Log($"TTS confirmation failed: {ttsEx.Message}");
                }
            });

            OnStateChanged?.Invoke("Ready");
        }
        catch (Exception ex)
        {
            Log($"Error: {ex}");
            OnStateChanged?.Invoke("Error");
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void Log(string msg)
    {
        Console.Error.WriteLine($"[copilot-voice] {msg}");
        OnLog?.Invoke(msg);
    }

    private static async Task SayAsync(string text)
    {
        if (OperatingSystem.IsMacOS())
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "say",
                Arguments = $"\"{text.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process.Start();
            await process.WaitForExitAsync();
        }
    }

    private static async Task CopyToClipboardAsync(string text)
    {
        if (OperatingSystem.IsMacOS())
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pbcopy",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process.Start();
            await process.StandardInput.WriteAsync(text);
            process.StandardInput.Close();
            await process.WaitForExitAsync();
        }
        else if (OperatingSystem.IsLinux())
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xclip",
                Arguments = "-selection clipboard",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process.Start();
            await process.StandardInput.WriteAsync(text);
            process.StandardInput.Close();
            await process.WaitForExitAsync();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hotkey?.Dispose();
        Animator.Dispose();
        _stt?.Dispose();
        _tts?.Dispose();
        _sessionManager.Dispose();
        _messageListener?.Dispose();
    }
}
