# Copilot Voice 🎤

Push-to-talk voice input for GitHub Copilot CLI.

**Hold a hotkey → speak → release → your words appear in Copilot CLI.**

No typing. No window switching. Just talk.

## Features

- **Push-to-talk** — Global hotkey triggers recording (default: `Ctrl+Shift+V`)
- **Azure Speech-to-Text** — Fast, accurate transcription via Azure Cognitive Services (free tier: 5 hours/month)
- **Session picker** — Lists active Copilot CLI sessions so you choose where to send input
- **Cross-platform** — Mac, Linux, Windows — single self-contained executable
- **Auto-send** — Transcription is typed into the selected session and Enter is pressed automatically

## Quick Start

```bash
# Download the latest release for your platform
# Or build from source:
dotnet publish -c Release -r osx-arm64 --self-contained

# Run
./copilot-voice --key YOUR_AZURE_SPEECH_KEY --region YOUR_REGION
```

## Requirements

- Azure Speech Services resource (F0 free tier works — 5 hours STT/month)
- Microphone access

## How It Works

1. Start `copilot-voice` — it runs in the background / system tray
2. It detects active Copilot CLI sessions (terminal windows)
3. Hold the push-to-talk hotkey
4. Speak your prompt
5. Release the hotkey
6. Audio is sent to Azure Speech-to-Text
7. Transcription is typed into the selected Copilot CLI session
8. Enter is pressed automatically — Copilot starts working

## Configuration

```
copilot-voice --help

Options:
  --key <key>         Azure Speech subscription key
  --region <region>   Azure Speech region (e.g., centralus)
  --hotkey <combo>    Push-to-talk hotkey (default: Ctrl+Shift+V)
  --session <id>      Target a specific session (skip picker)
  --list-sessions     List active Copilot CLI sessions and exit
```

## License

MIT
