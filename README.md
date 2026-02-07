# Copilot Voice 🎤
![CI](https://github.com/vbomfim/copilot-voice/actions/workflows/ci.yml/badge.svg)

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

## CI/CD Pipeline

Every push and pull request is validated automatically. Releases are built and published when you tag a version.

```
Push / PR to main ──► CI Workflow
                       ├── Build on macOS, Linux, Windows
                       ├── Run unit tests
                       └── Check code formatting

Push tag v* ─────────► Release Workflow
                       ├── Build self-contained binary per platform
                       │   ├── macOS ARM64  (.tar.gz)
                       │   ├── macOS x64    (.tar.gz)
                       │   ├── Linux x64    (.tar.gz)
                       │   └── Windows x64  (.zip)
                       └── Create GitHub Release with all assets
```

### CI — Quality Gate

Runs on every push to `main` and every PR:

- **Build** — compiles on all 3 operating systems
- **Test** — runs unit tests (integration tests excluded)
- **Format** — checks `dotnet format` compliance

A failing CI blocks the PR from merging.

### Release — Publishing a New Version

To publish a release with downloadable binaries for all platforms:

```bash
# 1. Make sure CI passes on main
git checkout main && git pull

# 2. Tag the new version (semantic versioning)
git tag v0.1.0

# 3. Push the tag — this triggers the release workflow
git push --tags

# 4. GitHub Actions automatically:
#    - Builds self-contained single-file executables for 4 targets
#    - Creates a GitHub Release at github.com/vbomfim/copilot-voice/releases
#    - Attaches all platform binaries as downloadable assets
```

Users can then download the binary for their platform from the [Releases](https://github.com/vbomfim/copilot-voice/releases) page — no .NET SDK required.

### Versioning

We use [Semantic Versioning](https://semver.org/):

- **v0.x.x** — pre-release, API may change
- **v1.0.0** — first stable release
- **MAJOR.MINOR.PATCH** — breaking.feature.fix

## License

MIT
