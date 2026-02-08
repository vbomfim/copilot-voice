#!/usr/bin/env bash
# run.sh — Build, publish as self-contained executable, and launch copilot-voice.
# Usage: bash scripts/run.sh [--stop]
set -euo pipefail

APP_NAME="CopilotVoice"
PUBLISH_DIR="/tmp/copilot-voice-publish"
LOG_FILE="/tmp/copilot-voice.log"
PID_FILE="/tmp/copilot-voice.pid"
DOTNET="/usr/local/share/dotnet/dotnet"
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CSPROJ="$PROJECT_DIR/src/$APP_NAME/$APP_NAME.csproj"

# Detect runtime identifier
if [[ "$(uname)" == "Darwin" ]]; then
    if [[ "$(uname -m)" == "arm64" ]]; then
        RID="osx-arm64"
    else
        RID="osx-x64"
    fi
elif [[ "$(uname)" == "Linux" ]]; then
    RID="linux-x64"
else
    RID="win-x64"
fi

stop_app() {
    local pids
    pids=$(pgrep -f "$APP_NAME" 2>/dev/null || true)
    for p in $pids; do
        echo "Stopping PID $p..."
        kill "$p" 2>/dev/null || true
    done
    sleep 1
    rm -f "$PID_FILE"
}

if [[ "${1:-}" == "--stop" ]]; then
    stop_app
    echo "Done."
    exit 0
fi

# Stop any running instance
stop_app

# Publish self-contained
echo "Publishing ($RID)..."
"$DOTNET" publish "$CSPROJ" -c Release -r "$RID" --self-contained \
    -p:PublishSingleFile=true -o "$PUBLISH_DIR" 2>&1 | tail -3

# Source env vars (Azure keys etc.)
source ~/.zprofile 2>/dev/null || true

# Launch
echo "" > "$LOG_FILE"
cd "$PUBLISH_DIR"
nohup "./$APP_NAME" >> "$LOG_FILE" 2>&1 &
APP_PID=$!
echo "$APP_PID" > "$PID_FILE"

# Wait for app to start
sleep 4
if curl -s --max-time 2 http://localhost:7701/health | grep -q ok; then
    echo "✅ Running (PID $APP_PID)"
    echo "   Executable: $PUBLISH_DIR/$APP_NAME"
    echo "   Log: $LOG_FILE"
    tail -3 "$LOG_FILE"
else
    echo "❌ Failed to start — check $LOG_FILE"
    tail -20 "$LOG_FILE"
    exit 1
fi
