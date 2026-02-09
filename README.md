# Copilot Voice 🎤🤖
![CI](https://github.com/vbomfim/copilot-voice/actions/workflows/ci.yml/badge.svg)

Your AI pair-programming buddy — with a voice and a face.

**Talk to Copilot CLI. It talks back. With an animated avatar.**

## What Is This?

Copilot Voice is a unified companion app for GitHub Copilot CLI that adds:

- 🎤 **Voice Input** — Push-to-talk: hold a hotkey, speak your prompt, release — it appears in Copilot CLI
- 🔊 **Voice Output** — Copilot's responses are spoken aloud via Azure Text-to-Speech
- 🤖 **Animated Avatar** — A visual buddy that reacts to speech, blinks, yawns, and shows expressions
- 🍅 **Pomodoro Timer** — Built-in focus/break timer with voice alerts and avatar state changes
- 🖥️ **Session Management** — Detects and targets active Copilot CLI sessions across terminals

One app. One install. Full bidirectional voice conversation with your AI pair programmer.

## Features

| Feature | Description |
|---------|-------------|
| **Push-to-talk** | Global hotkey (default: `Ctrl+Shift+V`) triggers recording |
| **Speech-to-Text** | Azure Cognitive Services — fast, accurate transcription (free tier: 5h/month) |
| **Text-to-Speech** | Copilot responses spoken aloud with configurable voice |
| **Animated Avatar** | Multiple themes (robot, waveform, symbols) with idle animations |
| **Pomodoro Timer** | Focus/break cycles with voice alerts and avatar expressions |
| **Session Picker** | Lists active Copilot CLI sessions — choose where to send input |
| **Cross-platform** | macOS, Linux, Windows — single self-contained executable |
| **Auto-send** | Transcription typed into session with Enter pressed automatically |

## Quick Start

```bash
# Download the latest release for your platform
# Or build from source:
dotnet publish -c Release -r osx-arm64 --self-contained

# Run with API key
./copilot-voice --key YOUR_AZURE_SPEECH_KEY --region centralus

# Or use environment variable
export AZURE_SPEECH_KEY=your-key
export AZURE_SPEECH_REGION=centralus
./copilot-voice

# Or sign in with Microsoft account (auto-detects resources)
./copilot-voice --auth signin
```

## Copilot CLI Integration

Copilot Voice works as a companion to GitHub Copilot CLI. Once running, Copilot CLI can speak to you, and you can speak to it.

There are **two ways** to teach Copilot CLI to use voice — **MCP** (recommended) or **Skills**. You can use both together.

| | MCP (Model Context Protocol) | Skills (Instructions file) |
|---|---|---|
| **How it works** | Copilot CLI connects to the voice app as an MCP server and gets native tools (`speak`, `listen`, `set_avatar`, etc.) | Instructions in `.github/copilot-instructions.md` tell the agent to call HTTP endpoints via `curl` |
| **Reliability** | ✅ High — tools appear automatically, agent can't forget | ⚠️ Medium — depends on agent reading and following instructions |
| **Discoverability** | ✅ Agent sees tools in its tool list | ❌ Agent must read and interpret free-text instructions |
| **Setup** | Add `mcp-config.json` (repo or user level) | Add text to `copilot-instructions.md` |
| **Works offline** | Only if voice app is running | Only if voice app is running |
| **Best for** | Primary integration — always-on voice tools | Supplementary guidance — behavioral rules, when/how to speak |

**Recommendation**: Use MCP for the tools + Skills for behavioral guidance (when to speak, tone, rules).

### Step 1: Start Copilot Voice

```bash
# Start the app (runs in the background with tray icon)
copilot-voice
```

### Step 2: Register your terminal session

From the terminal where you use Copilot CLI, register it so Copilot Voice knows where to send transcriptions:

```bash
copilot-voice --register                       # auto-detects terminal and working directory
copilot-voice --register --label "my-project"  # with a custom label
```

### Step 3: Connect Copilot CLI to voice (choose one or both)

#### Option A: MCP (Recommended)

MCP gives Copilot CLI native tools like `speak`, `listen`, `set_avatar`, `notify`, and `window`. The agent discovers them automatically.

**Per-repository setup** — add `.copilot/mcp-config.json` to your repo:

```json
{
  "mcpServers": {
    "copilot-voice": {
      "type": "http",
      "url": "http://localhost:7702/sse"
    }
  }
}
```

**User-level setup** — add to `~/.copilot/mcp-config.json` (applies to all repos):

```json
{
  "mcpServers": {
    "copilot-voice": {
      "type": "http",
      "url": "http://localhost:7702/sse"
    }
  }
}
```

Once configured, Copilot CLI will have these MCP tools available:

| Tool | Description |
|------|-------------|
| `speak` | Speak text aloud via TTS with avatar animation |
| `listen` | Start microphone listening, returns transcribed text |
| `set_avatar` | Change avatar expression (normal, thinking, speaking, listening, focused, relaxed, sleeping) |
| `notify` | Show notification with optional voice |
| `window` | Control avatar window (show, hide, toggle, topmost, move) |

#### Option B: Skills (Instructions file)

Skills are free-text instructions that guide the agent's behavior. Add to `.github/copilot-instructions.md` in your repo:

```markdown
## Voice Communication
Copilot Voice is running as a companion app. Use it to speak to the user.

### When to speak
- Status updates: "Build passed", "Tests done", "PR created"
- Questions: "Should I fix this or skip it? Check the terminal for details"
- Completion: "All done! Check the terminal for the summary"
- Errors: "Build failed, take a look at the terminal"

### How to speak (TTS with avatar animation)
If the `speak` MCP tool is available, use it. Otherwise fall back to curl:
curl -s -X POST http://localhost:7701/speak \
  -H "Content-Type: application/json" \
  -d '{"text":"Your short summary here"}'

### How to show a notification (visual only, no voice)
curl -s -X POST http://localhost:7701/bubble \
  -H "Content-Type: application/json" \
  -d '{"text":"Your notification here"}'

### Rules
- Always use voice for questions — if you need user input, speak the question.
- Keep it short: 1-2 sentences. Summarize, don't repeat everything.
- Details stay in the terminal — the user can always read the full output there.
- Don't narrate every step. Only speak on meaningful events.
- Use /speak for important things, /bubble for minor FYI.
- After speaking a question, also write it in the terminal so the user can reference it.
```

> **Tip**: You can use both MCP and Skills together without duplication. When MCP tools are available, the agent uses the native `speak` tool directly — it won't also call `curl`. The Skills file then provides *behavioral guidance* only (when to speak, tone, rules). If MCP is not configured, the agent falls back to the `curl` commands in the Skills file.

### Step 4: Use voice

- **Voice In**: Hold the hotkey (default: `Alt+Space`), speak, release — your words are transcribed and sent to Copilot CLI
- **Voice Out**: Copilot CLI uses the `speak` tool (MCP) or calls `/speak` (Skills) to talk back through the avatar

### HTTP API

All endpoints are on `http://localhost:7701` (used by Skills approach and direct integrations):

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Health check |
| `/speak` | POST | Speak text aloud via TTS with avatar animation |
| `/bubble` | POST | Show visual speech bubble (no audio) |
| `/register` | POST | Register a terminal session |

### MCP Server

The MCP server runs on `http://localhost:7702/sse` (SSE transport). Connect any MCP-compatible client to this URL.

## Requirements

- Azure Speech Services resource (F0 free tier: 5h STT + 500K chars TTS/month)
- Microphone access
- Speakers/headphones (for voice output)

## How It Works

```
┌─────────────────────────────────────────────────────────────────┐
│                      Copilot Voice App                          │
│                                                                 │
│  ┌──────────┐  ┌──────────┐  ┌───────────┐  ┌──────────────┐  │
│  │ Tray     │  │ Hotkey   │  │ Speech    │  │ Avatar       │  │
│  │ Icon/UI  │  │ Listener │  │ Engine    │  │ Engine       │  │
│  │(Avalonia)│  │(SharpHook│  │(Azure SDK)│  │(Animations,  │  │
│  │          │  │          │  │ STT + TTS │  │ Expressions) │  │
│  └────┬─────┘  └────┬─────┘  └─────┬─────┘  └──────┬───────┘  │
│       │              │              │               │           │
│       └──────────────┴──────────────┴───────────────┘           │
│                          │                                      │
│                    ┌─────┴─────┐    ┌──────────────┐            │
│                    │ App Core  │────│ Pomodoro     │            │
│                    │ (Program) │    │ Timer        │            │
│                    └─────┬─────┘    └──────────────┘            │
│                          │                                      │
│         ┌────────────────┼────────────────┐                     │
│         │                │                │                     │
│  ┌──────┴──────┐  ┌──────┴──────┐  ┌─────┴──────┐             │
│  │ Session     │  │ Input       │  │ Config     │             │
│  │ Detector    │  │ Sender      │  │ Manager    │             │
│  └─────────────┘  └──────┬──────┘  └────────────┘             │
│                          │                                      │
└──────────────────────────┼──────────────────────────────────────┘
                           │
                           ▼
                  ┌─────────────────┐
                  │ Copilot CLI     │
                  │ Terminal Session │
                  └─────────────────┘
```

### The Full Loop

1. Start `copilot-voice` — avatar appears, tray icon shows in menu bar
2. It detects active Copilot CLI sessions (terminal windows)
3. **Voice In**: Hold hotkey → speak → release → transcription sent to Copilot CLI
4. **Voice Out**: Copilot's response is spoken aloud via TTS, avatar animates
5. **Avatar**: Shows expressions (listening, thinking, speaking), blinks, yawns when idle
6. **Pomodoro**: Optional focus timer — avatar changes to focused/relaxed expressions

## Configuration

```
copilot-voice --help

Options:
  --key <key>         Azure Speech subscription key
  --region <region>   Azure Speech region (default: centralus)
  --auth <mode>       Auth mode: signin, apikey, or env (default: auto-detect)
  --hotkey <combo>    Push-to-talk hotkey (default: Ctrl+Shift+V)
  --voice <name>      TTS voice name (default: en-US-JennyNeural)
  --theme <name>      Avatar theme: robot, waveform, symbols
  --session <id>      Target a specific session (skip picker)
  --list-sessions     List active Copilot CLI sessions and exit
  --register          Register current terminal as a Copilot CLI session
  --label <name>      Custom label for --register (default: terminal window title)
  --mcp               Run as MCP server (stdio JSON-RPC)
  --pomodoro <cmd>    Start/stop pomodoro (start[:work:break] | stop)

Environment variables:
  AZURE_SPEECH_KEY    Azure Speech subscription key (priority over config)
  AZURE_SPEECH_REGION Azure Speech region
```

Config file: `~/.copilot-voice/config.json`

## Avatar Themes

```
🤖 Robot (default)          🌊 Waveform               ✦ Symbols
┌───────────────────┐      ┌───────────────────┐     ┌───────────────────┐
│     ◠◠◠◠◠         │      │                   │     │                   │
│   ┌─────────┐     │      │   ▁▂▃▅▂▁▂▃▅▂▁    │     │   ◆ ◇ ◆ ◇ ◆     │
│   │ ◕   ◕   │     │      │   ▁▂▃▅▂▁▂▃▅▂▁    │     │   ◇ ◆ ◇ ◆ ◇     │
│   │    ‿    │     │      │   ▔▔▔▔▔▔▔▔▔▔▔    │     │   ◆ ◇ ◆ ◇ ◆     │
│   └─────────┘     │      │                   │     │                   │
└───────────────────┘      └───────────────────┘     └───────────────────┘
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

Users download from the [Releases](https://github.com/vbomfim/copilot-voice/releases) page — no .NET SDK required.

### Versioning

[Semantic Versioning](https://semver.org/): **v0.x.x** pre-release → **v1.0.0** stable → **MAJOR.MINOR.PATCH**

## License

MIT
