using CopilotVoice.Audio;
using CopilotVoice.Bridge;
using CopilotVoice.Config;
using CopilotVoice.UI.Avatar;
using CopilotVoice.Voice;

namespace CopilotVoice;

/// <summary>
/// Centralizes all backend services and exposes events for the Avalonia UI.
/// New architecture: BridgeServer + VoiceLiveClient + PushToTalkController.
/// </summary>
public sealed class AppServices : IDisposable
{
    public AppConfig Config { get; }
    public AvatarState AvatarState { get; } = new();
    public AvatarAnimator Animator { get; } = new();

    private readonly ConfigManager _configManager;
    private Hotkey.HotkeyListener? _hotkey;
    private BridgeServer? _bridgeServer;
    private IVoiceLiveSession? _voiceLiveSession;
    private PushToTalkController? _pttController;
    private IMicCapture? _micCapture;
    private IAudioPlayer? _audioPlayer;
    private bool _disposed;
    private bool _hasMicrophone = true;
    private bool _isMuted;
    private CancellationTokenSource? _micMonitorCts;

    // UI events
    public event Action<string>? OnStateChanged;
    public event Action<string?, string?>? OnSpeechBubble;
    public event Action<string?>? OnTranscriptionUpdate;
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
    }

    public async Task StartAsync()
    {
        Log("Starting services...");

        // Check microphone availability and start polling
        _hasMicrophone = CheckMicrophoneAvailable();
        if (!_hasMicrophone)
        {
            Log("No microphone detected");
            OnMicAvailabilityChanged?.Invoke(false);
        }
        StartMicMonitor();

        // Hotkey
        try
        {
            _hotkey = new Hotkey.HotkeyListener(Config.Hotkey);
            _hotkey.OnError += msg => Log($"Hotkey: {msg}");
            _hotkey.Start();
            Log($"Hotkey: {Config.Hotkey}");
        }
        catch (Exception ex)
        {
            Log($"Hotkey failed: {ex.Message}");
        }

        // Start avatar idle animation
        Animator.StartIdleLoop();

        // Voice Live API + Push-to-Talk pipeline
        await StartVoiceLivePipelineAsync();

        OnStateChanged?.Invoke("Ready");
        Log("Ready!");
    }

    /// <summary>
    /// Initializes the Voice Live API pipeline when credentials are configured:
    /// BridgeServer → VoiceLiveClient → PushToTalkController, wired to HotkeyListener.
    /// </summary>
    private async Task StartVoiceLivePipelineAsync()
    {
        // 1. Start the HTTP bridge server (for CLI extension communication)
        try
        {
            _bridgeServer = new BridgeServer();
            await _bridgeServer.StartAsync();
            Log("Bridge server: http://127.0.0.1:7701");
        }
        catch (Exception ex)
        {
            Log($"Bridge server failed: {ex.Message}");
            return;
        }

        // 2. Connect to Voice Live API (if credentials configured)
        var endpoint = Config.VoiceLiveEndpoint;
        var apiKey = Config.VoiceLiveKey
            ?? Environment.GetEnvironmentVariable("AZURE_VOICELIVE_KEY");
        var envEndpoint = Environment.GetEnvironmentVariable("AZURE_VOICELIVE_ENDPOINT");
        if (string.IsNullOrEmpty(endpoint) && !string.IsNullOrEmpty(envEndpoint))
            endpoint = envEndpoint;

        if (!string.IsNullOrEmpty(endpoint))
        {
            try
            {
                var voiceConfig = new VoiceLiveConfig(
                    Endpoint: endpoint,
                    ApiKey: apiKey,
                    Model: Config.VoiceLiveModel,
                    Voice: Config.VoiceLiveVoice
                );

                var client = new VoiceLiveClient();
                _voiceLiveSession = await client.ConnectAsync(voiceConfig);
                Log($"Voice Live API: connected ({Config.VoiceLiveModel})");
            }
            catch (Exception ex)
            {
                Log($"Voice Live API: {ex.Message}");
            }
        }
        else
        {
            Log("Voice Live API: no endpoint configured (push-to-talk disabled)");
        }

        // 3. Create PushToTalkController if voice session is available
        if (_voiceLiveSession is not null)
        {
            _micCapture = new MicCapture();
            _audioPlayer = new Audio.AudioPlayer();

            _pttController = new PushToTalkController(
                _voiceLiveSession,
                _micCapture,
                _audioPlayer,
                _bridgeServer.SessionBridge);

            _pttController.StateChanged += state =>
            {
                Log($"PushToTalk: {state}");
                OnStateChanged?.Invoke(state.ToString());
            };

            // 4. Wire hotkey to push-to-talk controller
            if (_hotkey is not null)
            {
                _hotkey.OnPushToTalkStart += _pttController.OnHotkeyPressed;
                _hotkey.OnPushToTalkStop += _pttController.OnHotkeyReleased;
                Log("Hotkey → PushToTalkController wired");
            }

            // 5. Wire function call handler for Voice Live API
            var functionHandler = new FunctionCallHandler(
                workspaceRoot: Environment.CurrentDirectory,
                sessionBridge: new BridgeClientAdapter(_bridgeServer.SessionBridge));

            _voiceLiveSession.FunctionCallReceived += async call =>
            {
                try
                {
                    var result = await functionHandler.HandleAsync(call);
                    await _voiceLiveSession.SendFunctionResultAsync(call.CallId, result);
                    Log($"Function call handled: {call.Name}");
                }
                catch (Exception ex)
                {
                    Log($"Function call error: {ex.Message}");
                }
            };

            await _pttController.StartAsync();
            Log("PushToTalkController: started");
        }
    }

    // Public wrappers for UI push-to-talk button
    public void OnMicButtonDown() => _pttController?.OnHotkeyPressed();
    public void OnMicButtonUp() => _pttController?.OnHotkeyReleased();

    public void ChangeVoice(string voiceName)
    {
        var previousVoice = Config.VoiceName;

        Config.VoiceName = voiceName;
        try
        {
            _configManager.Save(Config);
        }
        catch (Exception ex)
        {
            Log($"Config save error: {ex.Message}");
            Config.VoiceName = previousVoice;
            return;
        }

        Log($"Voice changed to: {voiceName}");
        OnVoiceChanged?.Invoke(voiceName);
    }

    public bool IsMuted => _isMuted;

    public void ToggleMute()
    {
        _isMuted = !_isMuted;
        Log($"Mute: {(_isMuted ? "ON" : "OFF")}");
        if (_isMuted)
        {
            Animator.SetExpression(AvatarExpression.Muted);
        }
        else
        {
            Animator.SetExpression(AvatarExpression.Normal);
        }
        OnMuteChanged?.Invoke(_isMuted);
    }

    private void Log(string msg)
    {
        Console.Error.WriteLine($"[copilot-voice] {msg}");
        OnLog?.Invoke(msg);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _micMonitorCts?.Cancel();
        _pttController?.StopAsync().Wait(2000);
        _micCapture?.Dispose();
        _audioPlayer?.Dispose();
        _voiceLiveSession?.DisposeAsync().AsTask().Wait(2000);
        _bridgeServer?.DisposeAsync().AsTask().Wait(2000);
        _hotkey?.Dispose();
        Animator.Dispose();
    }

    private static bool CheckMicrophoneAvailable()
    {
        if (OperatingSystem.IsMacOS())
            return CheckMicrophoneMacOS();
        if (OperatingSystem.IsWindows())
            return CheckMicrophoneWindows();
        return true;
    }

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
            const uint kAudioHardwarePropertyDefaultInputDevice = 0x64496E20;
            const uint kAudioObjectPropertyScopeGlobal = 0x676C6F62;
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
