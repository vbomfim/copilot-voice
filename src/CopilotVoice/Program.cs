using CopilotVoice.Audio;
using CopilotVoice.Config;
using CopilotVoice.Input;
using CopilotVoice.Messaging;
using CopilotVoice.Sessions;
using CopilotVoice.UI;
using CopilotVoice.UI.Avatar;

namespace CopilotVoice;

class Program
{
    static async Task Main(string[] args)
    {
        var cliArgs = CliArgs.Parse(args);
        if (cliArgs.ShowHelp) { CliArgs.PrintHelp(); return; }

        var configManager = new ConfigManager();
        var config = configManager.LoadOrCreate();
        cliArgs.ApplyOverrides(config);

        var sessionDetector = new SessionDetector();

        // One-shot: list sessions
        if (cliArgs.ListSessions)
        {
            var sessions = sessionDetector.DetectSessions();
            if (sessions.Count == 0) { Console.WriteLine("No active Copilot CLI sessions found."); return; }
            foreach (var s in sessions)
                Console.WriteLine($"  {s.TerminalApp} — {s.Label} (PID: {s.ProcessId})");
            return;
        }

        Console.WriteLine("🎤🤖 Copilot Voice — Starting...");

        // ── Auth ────────────────────────────────────────────────
        var authProvider = new AzureAuthProvider();
        try
        {
            var (_, region) = authProvider.Resolve(config);
            Console.WriteLine($"  ✅ Azure Speech: {region}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️  Auth: {ex.Message}");
            Console.WriteLine("  Run with --key <key> or set AZURE_SPEECH_KEY");
            return;
        }

        // ── Sessions ────────────────────────────────────────────
        var initialSessions = sessionDetector.DetectSessions();
        Console.WriteLine($"  📡 Found {initialSessions.Count} session(s)");

        var sessionManager = new SessionManager(sessionDetector);
        sessionManager.OnTargetChanged += s =>
            Console.WriteLine($"  📡 Target: {s?.Label ?? "none"}");
        sessionManager.StartWatching();

        // ── Input Sender ────────────────────────────────────────
        IInputSender inputSender;
        try
        {
            inputSender = InputSenderFactory.Create();
            Console.WriteLine($"  ⌨️  Input sender: {inputSender.GetType().Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️  Input sender: {ex.Message}");
            return;
        }

        // ── Avatar ──────────────────────────────────────────────
        var avatarState = new AvatarState();
        var avatarAnimator = new AvatarAnimator();
        ConsoleAvatarRenderer? avatarRenderer = null;

        if (config.ShowAvatar)
        {
            avatarRenderer = new ConsoleAvatarRenderer(avatarState);
            avatarAnimator.OnExpressionChanged += expr => avatarRenderer.RenderExpression(expr);
        }

        // ── TTS ─────────────────────────────────────────────────
        TextToSpeechEngine? tts = null;
        if (config.EnableVoiceOutput)
        {
            try
            {
                tts = new TextToSpeechEngine(config);
                tts.OnSpeechStarted += () =>
                {
                    avatarAnimator.RecordInteraction();
                    avatarRenderer?.RenderExpression(AvatarExpression.Speaking);
                };
                tts.OnSpeechFinished += () =>
                    avatarRenderer?.RenderExpression(AvatarExpression.Normal);
                tts.OnError += err => Console.WriteLine($"  ⚠️  TTS: {err}");
                Console.WriteLine($"  🔊 TTS: {config.VoiceName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠️  TTS init failed: {ex.Message} (continuing without voice output)");
            }
        }

        // ── STT ─────────────────────────────────────────────────
        var stt = new PushToTalkRecognizer(config);
        var indicator = new RecordingIndicator();
        var trayIcon = new TrayIcon();

        stt.OnPartialResult += text =>
        {
            indicator.UpdatePartialText(text);
            avatarAnimator.RecordInteraction();
        };
        stt.OnError += err => Console.WriteLine($"  ⚠️  STT: {err}");

        // ── Message Listener (inbound TTS from other sessions) ──
        MessageListener? messageListener = null;
        try
        {
            messageListener = new MessageListener();
            messageListener.OnMessageReceived += async msg =>
            {
                if (tts != null)
                {
                    avatarRenderer?.ShowSpeechBubble(msg.Text, msg.SessionLabel);
                    await tts.SpeakAsync(msg.Text);
                    avatarRenderer?.ClearSpeechBubble();
                }
            };
            messageListener.Start();
            Console.WriteLine("  📨 Message listener: http://localhost:7701");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️  Message listener: {ex.Message}");
        }

        // ── Pomodoro ────────────────────────────────────────────
        Pomodoro.PomodoroTimer? pomodoro = null;
        // Will be started via future CLI command or UI action

        // ── Hotkey ──────────────────────────────────────────────
        Hotkey.HotkeyListener hotkey;
        try { hotkey = new Hotkey.HotkeyListener(config.Hotkey); }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"  ⚠️  Invalid hotkey \"{config.Hotkey}\": {ex.Message}");
            return;
        }

        var isRecording = false;

        hotkey.OnPushToTalkStart += async () =>
        {
            if (isRecording) return;
            isRecording = true;

            trayIcon.SetState(TrayState.Recording);
            indicator.Show();
            avatarAnimator.RecordInteraction();
            avatarRenderer?.RenderExpression(AvatarExpression.Listening);

            try { await stt.StartRecordingAsync(); }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠️  Mic error: {ex.Message}");
                trayIcon.SetState(TrayState.Error);
                isRecording = false;
            }
        };

        hotkey.OnPushToTalkStop += async () =>
        {
            if (!isRecording) return;
            isRecording = false;

            trayIcon.SetState(TrayState.Transcribing);
            avatarRenderer?.RenderExpression(AvatarExpression.Thinking);

            try
            {
                var text = await stt.StopRecordingAndTranscribeAsync();
                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("  ⚠️  No speech detected");
                    trayIcon.SetState(TrayState.Idle);
                    avatarRenderer?.RenderExpression(AvatarExpression.Normal);
                    return;
                }

                await indicator.ShowFinalAndHideAsync(text);

                // Send text to target session
                var target = sessionManager.GetTargetSession();
                if (target != null)
                {
                    await inputSender.SendTextAsync(target, text, config.AutoPressEnter);
                    Console.WriteLine($"  ✅ Sent to {target.Label}");
                }
                else
                {
                    Console.WriteLine("  ⚠️  No target session — text not sent");
                    trayIcon.SetState(TrayState.NoSession);
                }

                trayIcon.SetState(TrayState.Idle);
                avatarRenderer?.RenderExpression(AvatarExpression.Normal);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠️  Error: {ex.Message}");
                trayIcon.SetState(TrayState.Error);
                avatarRenderer?.RenderExpression(AvatarExpression.Normal);
            }
        };

        // ── Start ───────────────────────────────────────────────
        Console.WriteLine($"  ⌨️  Hotkey: {config.Hotkey}");
        Console.WriteLine("  Ready! Hold hotkey to speak. Ctrl+C to quit.");
        Console.WriteLine();

        trayIcon.Show();

        if (avatarRenderer != null)
        {
            avatarRenderer.Initialize();
            avatarAnimator.StartIdleLoop();
        }

        // Wait for Ctrl+C
        var exitTcs = new TaskCompletionSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            exitTcs.TrySetResult();
        };

        hotkey.Start();
        await exitTcs.Task;

        // ── Shutdown ────────────────────────────────────────────
        Console.WriteLine("\n👋 Shutting down...");
        hotkey.Dispose();
        avatarAnimator.Dispose();
        avatarRenderer?.Dispose();
        stt.Dispose();
        tts?.Dispose();
        trayIcon.Dispose();
        sessionManager.Dispose();
        messageListener?.Dispose();
        pomodoro?.Dispose();

        Console.WriteLine("Goodbye!");
    }
}
