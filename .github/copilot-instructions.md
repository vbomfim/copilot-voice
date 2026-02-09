# Copilot Voice — Agent Instructions

## Before You Start
1. Read [README.md](../README.md) for architecture and design decisions
2. Read [WIREFRAMES.md](../WIREFRAMES.md) for UI design
3. Read [CONTRIBUTING.md](../CONTRIBUTING.md) for git workflow
4. Check the issue description for acceptance criteria and linked docs

## Voice Communication
Copilot Voice is running as a companion app. Use it to **speak** to the user:

### When to speak
- **Status updates**: "Build passed", "Tests done, 2 failures", "PR created"
- **Questions**: "Should I fix this test or skip it? Check the terminal for details"
- **Completion**: "All done! PR 47 is ready for review"
- **Errors**: "Build failed, check the terminal"

### How to speak
```bash
curl -s -X POST http://localhost:7701/speak \
  -H "Content-Type: application/json" \
  -d '{"text":"Your short summary here"}'
```

### How to show a bubble (no voice, just visual)
```bash
curl -s -X POST http://localhost:7701/bubble \
  -H "Content-Type: application/json" \
  -d '{"text":"Your notification here"}'
```

### Rules
- **Keep it short**: 1-2 sentences max. Summarize, don't repeat everything.
- **Details stay in terminal**: The user can always read the full output in the terminal.
- **Don't narrate every step**: Only speak on meaningful events (done, error, question, milestone).
- **Use bubble for minor updates**: Use `/speak` for important things, `/bubble` for FYI.

## Development Rules
- **Branch per issue**: `issue-<N>-<short-name>`, never commit to `main`
- **Frequent commits**: one per logical step, not one giant commit
- **Build after every change**: `dotnet build` must pass
- **Draft PR early**: create draft PR after first commit for visibility
- **PR links issue**: include `Closes #N` in PR body
- **Address reviews**: fix valid comments, reply to disagreements, never ignore

## Auth Configuration
Azure Speech credentials resolve in this priority:
1. CLI `--key` flag (highest)
2. Environment variable `AZURE_SPEECH_KEY` / `AZURE_SPEECH_REGION`
3. Config file `~/.copilot-voice/config.json`
4. Interactive Microsoft Sign-In (lowest)

## Project Structure
```
src/CopilotVoice/
├── Config/      — AppConfig, ConfigManager, AzureAuthProvider
├── Audio/       — PushToTalkRecognizer (STT), TextToSpeechEngine (TTS)
├── Hotkey/      — HotkeyListener, HotkeyRecorder (SharpHook)
├── Sessions/    — CopilotSession, SessionDetector, SessionManager
├── Input/       — IInputSender, MacInputSender, InputSenderFactory
├── Messaging/   — MessageListener, InboundMessage
├── Pomodoro/    — PomodoroTimer
└── UI/
    └── Avatar/  — AvatarWidget, AvatarAnimator, Themes/
```

## Key Design Decisions
- **Avalonia** for all cross-platform UI (tray, avatar, settings)
- **SharpHook** for global hotkey (works without window focus)
- **Azure Speech SDK** for both STT and TTS (same credentials)
- **Session auto-follow**: targets whichever Copilot CLI has focus; lock mode pins to one
- **Multi-session TTS**: any Copilot session can send messages to the avatar, identified by working directory label
