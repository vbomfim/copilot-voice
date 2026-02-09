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
    private Mcp.McpServer? _mcpServer;
    private Mcp.McpSseTransport? _mcpSseTransport;
    private bool _isRecording;
    private bool _isBusy;
    private bool _disposed;
    private bool _hasMicrophone = true;
    private bool _isMuted;
    private CancellationTokenSource? _micMonitorCts;

    // UI events
    public event Action<string>? OnStateChanged;
    public event Action<string?, string?>? OnSpeechBubble;
    public event Action<string?>? OnTranscriptionUpdate; // user's voice (partial STT)
    public event Action<string?>? OnTargetSession;
    public event Action<string?, TimeSpan>? OnTimerTick;
    public event Action<List<CopilotSession>>? OnSessionsRefreshed;
    public event Action<string>? OnLog;
    public event Action<bool>? OnMicAvailabilityChanged;
    public event Action<string>? OnVoiceChanged;
    public event Action<bool>? OnMuteChanged;
    // Window control: action, x, y, position → result
    public event Func<string, int?, int?, string?, Task<string>>? OnWindowControl;

    public AppServices()
    {
        _configManager = new ConfigManager();
        Config = _configManager.LoadOrCreate();

        // Apply env var overrides and persist to config
        var envKey = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
        var envRegion = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION");
        bool configChanged = false;
        if (!string.IsNullOrEmpty(envKey) && Config.AzureSpeechKey != envKey)
        {
            Config.AzureSpeechKey = envKey;
            Config.AuthMode = AuthMode.Env;
            configChanged = true;
        }
        if (!string.IsNullOrEmpty(envRegion) && Config.AzureSpeechRegion != envRegion)
        {
            Config.AzureSpeechRegion = envRegion;
            configChanged = true;
        }
        if (configChanged)
            _configManager.Save(Config);

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
            OnSpeechBubble?.Invoke($"Auth error: {ex.Message}", null);
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
                _voiceName = Config.VoiceName;
                _azureSpeechKey = Config.AzureSpeechKey;
                _azureSpeechRegion = Config.AzureSpeechRegion;
            }
            catch (Exception ex)
            {
                Log($"TTS init failed: {ex.Message}");
            }
        }

        // Check microphone availability and start polling
        _hasMicrophone = CheckMicrophoneAvailable();
        if (!_hasMicrophone)
        {
            Log("No microphone detected");
            OnMicAvailabilityChanged?.Invoke(false);
        }
        StartMicMonitor();

        // STT
        _stt = new PushToTalkRecognizer(Config);
        _stt.OnPartialResult += text =>
        {
            Animator.RecordInteraction();
            OnTranscriptionUpdate?.Invoke(text);
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
                    if (!_isMuted)
                        await SayStaticAsync(msg.Text);
                    OnSpeechBubble?.Invoke(null, null);
                }
                catch (Exception ex) { Log($"Message handler error: {ex.Message}"); }
            };
            _messageListener.OnSpeakReceived += async msg =>
            {
                if (_isMuted) return;
                OnSpeechBubble?.Invoke(msg.Text, null);
                await SayStaticAsync(msg.Text);
                OnSpeechBubble?.Invoke(null, null);
            };
            _messageListener.OnBubbleReceived += msg =>
            {
                OnSpeechBubble?.Invoke(msg.Text, null);
            };
            _messageListener.OnRegisterReceived += async reg =>
            {
                var session = _sessionManager.RegisterSession(reg);
                Log($"Session registered: {session.Label} (PID {session.ProcessId})");
                _sessionManager.LockToSession(session);
                OnSessionsRefreshed?.Invoke(_sessionManager.GetAllSessions());
                return session.Label;
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

        // MCP TCP server — lets Copilot CLI connect via TCP on port 7702
        try
        {
            _mcpServer = new Mcp.McpServer();
            _mcpServer.OnLog += msg => Log(msg);
            Mcp.McpToolHandler.OnSpeak = async (text, _voice) => await SayStaticAsync(text);
            Mcp.McpToolHandler.OnListen = async (duration, _lang) =>
            {
                if (_stt == null) return "Listening not available";
                var tcs = new TaskCompletionSource<string>();
                void handler(string text) { tcs.TrySetResult(text); }
                _stt.OnFinalResult += handler;
                try
                {
                    OnStateChanged?.Invoke("Recording");
                    await _stt.StartRecordingAsync();
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(duration));
                    cts.Token.Register(() => tcs.TrySetResult(""));
                    var result = await tcs.Task;
                    await _stt.StopRecordingAndTranscribeAsync();
                    OnStateChanged?.Invoke("Ready");
                    return result;
                }
                finally { _stt.OnFinalResult -= handler; }
            };
            Mcp.McpToolHandler.OnNotify = async (message, speak) =>
            {
                OnSpeechBubble?.Invoke(message, null);
                if (speak) await SayStaticAsync(message);
            };
            Mcp.McpToolHandler.OnSetAvatar = expr =>
            {
                OnStateChanged?.Invoke(expr);
            };
            Mcp.McpToolHandler.OnWindowControl = async (action, x, y, position) =>
            {
                if (OnWindowControl != null)
                    return await OnWindowControl(action, x, y, position);
                return "Window control not available";
            };
            _mcpSseTransport = new Mcp.McpSseTransport(_mcpServer, 7702);
            _mcpSseTransport.OnLog += msg => Log(msg);
            _mcpSseTransport.Start();
            Log("MCP SSE server: http://localhost:7702/sse");
        }
        catch (Exception ex)
        {
            Log($"MCP server: {ex.Message}");
        }

        // Start avatar idle animation
        Animator.StartIdleLoop();
        OnStateChanged?.Invoke("Ready");
        Log("Ready!");
    }

    private readonly SemaphoreSlim _recordLock = new(1, 1);

    // Public wrappers for UI push-to-talk button
    public void OnMicButtonDown() => OnHotkeyDown();
    public void OnMicButtonUp() => OnHotkeyUp();

    private async void OnHotkeyDown()
    {
        if (_isRecording || _isBusy || _stt == null) return;

        if (!_hasMicrophone)
        {
            OnSpeechBubble?.Invoke("No microphone available. Please connect a mic.", null);
            return;
        }

        if (!_recordLock.Wait(0)) return; // non-blocking trylock
        try
        {
            if (_isRecording) return; // double-check under lock
            _isRecording = true;

            // Stop any active TTS when user starts recording
            _tts?.Stop();

            Log("Hotkey pressed — starting recording");
            OnStateChanged?.Invoke("Recording");
            Animator.RecordInteraction();

            await _stt.StartRecordingAsync();
        }
        catch (Exception ex)
        {
            var friendly = ex.Message.Contains("0x15") || ex.Message.Contains("MIC_ERROR")
                ? "No microphone available. Please connect a mic and try again."
                : ex.Message.Contains("0x5") || ex.Message.Contains("PERMISSION")
                ? "Microphone permission denied. Grant access in System Settings → Privacy → Microphone."
                : $"Mic error: {ex.Message}";
            Log($"Mic error: {ex.Message}");
            OnStateChanged?.Invoke("Error");
            OnSpeechBubble?.Invoke(friendly, null);
            if (ex.Message.Contains("0x15") || ex.Message.Contains("MIC_ERROR"))
            {
                _hasMicrophone = false;
                OnMicAvailabilityChanged?.Invoke(false);
            }
            _isRecording = false;
        }
        finally
        {
            _recordLock.Release();
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

            // Always paste into console (delivers text as a prompt)
            var target = _sessionManager.GetTargetSession();
            if (target != null && _inputSender != null)
            {
                try
                {
                    Log($"Pasting to {target.Label}: \"{text}\"");
                    await _inputSender.SendTextAsync(target, text, Config.AutoPressEnter);
                    Log($"Pasted to {target.Label} OK");
                }
                catch (Exception sendEx)
                {
                    Log($"Send failed: {sendEx.Message} — text is in clipboard");
                }
            }
            else
            {
                Log("No target session — text is in clipboard (Cmd+V to paste)");
            }

            OnStateChanged?.Invoke("Ready");
            // Clear transcription after a short delay
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                OnTranscriptionUpdate?.Invoke(null);
            });
        }
        catch (Exception ex)
        {
            Log($"Error: {ex}");
            OnStateChanged?.Invoke("Error");
            OnSpeechBubble?.Invoke($"Error: {ex.Message}", null);
        }
        finally
        {
            _isBusy = false;
        }
    }

    // Session management for tray menu
    public bool IsSessionLocked => _sessionManager.IsLocked;

    public void ToggleSessionLock()
    {
        _sessionManager.ToggleLock();
        Log($"Session lock: {(IsSessionLocked ? "locked" : "auto")}");
    }

    public void SelectSession(CopilotSession session)
    {
        _sessionManager.SelectSession(session);
        Log($"Selected session: {session.Label}");
        ActivateSessionWindow(session);
    }

    private static void ActivateSessionWindow(CopilotSession session)
    {
        if (!OperatingSystem.IsMacOS() || string.IsNullOrEmpty(session.TerminalApp))
            return;

        try
        {
            using var proc = new System.Diagnostics.Process();
            proc.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "osascript",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            proc.Start();
            proc.StandardInput.Write($"tell application \"{session.TerminalApp}\" to activate");
            proc.StandardInput.Close();
            proc.WaitForExit(3000);
        }
        catch { /* best effort */ }
    }

    public void RefreshSessions()
    {
        _sessionDetector.DetectSessions();
        var sessions = _sessionManager.GetAllSessions();
        Log($"Refreshed: {sessions.Count} session(s)");
        OnSessionsRefreshed?.Invoke(sessions);
    }

    public void ChangeVoice(string voiceName)
    {
        Config.VoiceName = voiceName;
        _configManager.Save(Config);

        // Recreate TTS engine with new voice
        _tts?.Dispose();
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
            _voiceName = Config.VoiceName;
            Log($"Voice changed to: {voiceName}");
            OnVoiceChanged?.Invoke(voiceName);
        }
        catch (Exception ex)
        {
            Log($"Voice change error: {ex.Message}");
        }
    }

    public bool IsMuted => _isMuted;

    public void ToggleMute()
    {
        _isMuted = !_isMuted;
        Log($"Mute: {(_isMuted ? "ON" : "OFF")}");
        if (_isMuted)
            _tts?.Stop();
        OnMuteChanged?.Invoke(_isMuted);
    }

    private void Log(string msg)
    {
        Console.Error.WriteLine($"[copilot-voice] {msg}");
        OnLog?.Invoke(msg);
    }

    private static string _voiceName = "en-US-AndrewMultilingualNeural";
    private static string? _azureSpeechKey;
    private static string? _azureSpeechRegion;

    public static async Task SayStaticAsync(string text)
    {
        Console.WriteLine($"[TTS] SayStaticAsync called: \"{text[..Math.Min(text.Length, 50)]}...\"");
        var tmpFile = Path.Combine(Path.GetTempPath(), $"copilot-voice-tts-{Guid.NewGuid():N}.wav");
        try
        {
            // Try Azure TTS first (cross-platform)
            var key = _azureSpeechKey
                ?? Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
            var region = _azureSpeechRegion
                ?? Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION")
                ?? "centralus";
            Console.WriteLine($"[TTS] Azure key present: {!string.IsNullOrEmpty(key)}, region: {region}, voice: {_voiceName}");

            if (!string.IsNullOrEmpty(key))
            {
                var speechConfig = Microsoft.CognitiveServices.Speech.SpeechConfig.FromSubscription(key, region);
                speechConfig.SpeechSynthesisVoiceName = _voiceName;
                using var audioConfig = Microsoft.CognitiveServices.Speech.Audio.AudioConfig.FromWavFileOutput(tmpFile);
                using var synthesizer = new Microsoft.CognitiveServices.Speech.SpeechSynthesizer(speechConfig, audioConfig);
                var result = await synthesizer.SpeakTextAsync(text);
                if (result.Reason == Microsoft.CognitiveServices.Speech.ResultReason.SynthesizingAudioCompleted)
                {
                    Console.WriteLine($"[TTS] Azure synthesis OK, playing {tmpFile}");
                    await PlayAudioFileAsync(tmpFile);
                    return;
                }
                Console.Error.WriteLine($"[TTS] Azure failed: {result.Reason}");
            }

            // Fallback: macOS say command
            if (OperatingSystem.IsMacOS())
            {
                var aiffFile = Path.ChangeExtension(tmpFile, ".aiff");
                using var sayProcess = new System.Diagnostics.Process();
                sayProcess.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "say",
                    Arguments = $"-o \"{aiffFile}\" \"{text.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                sayProcess.Start();
                await sayProcess.WaitForExitAsync();
                await PlayAudioFileAsync(aiffFile);
                try { File.Delete(aiffFile); } catch { }
            }
        }
        finally
        {
            try { File.Delete(tmpFile); } catch { }
        }
    }

    private static async Task PlayAudioFileAsync(string filePath)
    {
        string player;
        string args;

        if (OperatingSystem.IsMacOS())
        {
            player = "afplay";
            args = $"\"{filePath}\"";
        }
        else if (OperatingSystem.IsLinux())
        {
            player = "aplay";
            args = $"\"{filePath}\"";
        }
        else if (OperatingSystem.IsWindows())
        {
            player = "powershell";
            args = $"-c \"(New-Object Media.SoundPlayer '{filePath}').PlaySync()\"";
        }
        else return;

        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = player,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        process.Start();
        await process.WaitForExitAsync();
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
        _micMonitorCts?.Cancel();
        _mcpSseTransport?.DisposeAsync().AsTask().Wait(2000);
        _hotkey?.Dispose();
        Animator.Dispose();
        _stt?.Dispose();
        _tts?.Dispose();
        _sessionManager.Dispose();
        _messageListener?.Dispose();
        _mcpServer?.DisposeAsync().AsTask().Wait(2000);
    }

    private static bool CheckMicrophoneAvailable()
    {
        if (OperatingSystem.IsMacOS())
            return CheckMicrophoneMacOS();
        if (OperatingSystem.IsWindows())
            return CheckMicrophoneWindows();
        // Linux: assume available (no reliable lightweight check)
        return true;
    }

    // macOS: CoreAudio P/Invoke — checks if a default input device exists without opening it
    [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/CoreAudio.framework/CoreAudio")]
    private static extern int AudioObjectGetPropertyData(
        uint objectID, ref CoreAudioPropertyAddress address,
        uint qualifierDataSize, IntPtr qualifierData,
        ref uint dataSize, out uint data);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct CoreAudioPropertyAddress
    {
        public uint mSelector;
        public uint mScope;
        public uint mElement;
    }

    private static bool CheckMicrophoneMacOS()
    {
        try
        {
            const uint kAudioHardwarePropertyDefaultInputDevice = 0x64496E20; // 'dIn '
            const uint kAudioObjectPropertyScopeGlobal = 0x676C6F62;          // 'glob'
            const uint kAudioObjectPropertyElementMain = 0;
            const uint kAudioObjectSystemObject = 1;
            const uint kAudioObjectUnknown = 0;

            var address = new CoreAudioPropertyAddress
            {
                mSelector = kAudioHardwarePropertyDefaultInputDevice,
                mScope = kAudioObjectPropertyScopeGlobal,
                mElement = kAudioObjectPropertyElementMain
            };
            uint size = 4;
            int status = AudioObjectGetPropertyData(kAudioObjectSystemObject, ref address, 0, IntPtr.Zero, ref size, out uint deviceID);
            return status == 0 && deviceID != kAudioObjectUnknown;
        }
        catch { return false; }
    }

    // Windows: lightweight check via winmm.dll
    [System.Runtime.InteropServices.DllImport("winmm.dll")]
    private static extern uint waveInGetNumDevs();

    private static bool CheckMicrophoneWindows()
    {
        try { return waveInGetNumDevs() > 0; }
        catch { return true; }
    }

    private void StartMicMonitor()
    {
        _micMonitorCts = new CancellationTokenSource();
        var ct = _micMonitorCts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(5000, ct); } catch { break; }
                var available = CheckMicrophoneAvailable();
                if (available != _hasMicrophone)
                {
                    _hasMicrophone = available;
                    Log(available ? "Microphone connected" : "Microphone disconnected");
                    OnMicAvailabilityChanged?.Invoke(available);
                    if (available)
                        OnStateChanged?.Invoke("Ready");
                }
            }
        }, ct);
    }
}
