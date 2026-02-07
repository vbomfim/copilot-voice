# Copilot Voice — Agent Instructions

## Before You Start
1. Read [README.md](../README.md) for architecture and design decisions
2. Read [WIREFRAMES.md](../WIREFRAMES.md) for UI design
3. Read [CONTRIBUTING.md](../CONTRIBUTING.md) for git workflow
4. Check the issue description for acceptance criteria and linked docs

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
