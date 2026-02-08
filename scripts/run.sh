#!/usr/bin/env bash
# run.sh — Build, copy to /tmp, and launch copilot-voice with auto-restart.
# Usage: bash scripts/run.sh [--stop]
set -euo pipefail

APP_NAME="CopilotVoice"
RUN_DIR="/tmp/copilot-voice-run"
LOG_FILE="/tmp/copilot-voice.log"
PID_FILE="/tmp/copilot-voice.pid"
WATCHDOG_PID_FILE="/tmp/copilot-voice-watchdog.pid"
DOTNET="/usr/local/share/dotnet/dotnet"
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CSPROJ="$PROJECT_DIR/src/$APP_NAME/$APP_NAME.csproj"

stop_app() {
    # Stop watchdog
    if [[ -f "$WATCHDOG_PID_FILE" ]]; then
        local wpid
        wpid=$(cat "$WATCHDOG_PID_FILE")
        if ps -p "$wpid" > /dev/null 2>&1; then
            echo "Stopping watchdog (PID $wpid)..."
            kill "$wpid" 2>/dev/null || true
        fi
        rm -f "$WATCHDOG_PID_FILE"
    fi
    # Stop app
    local pids
    pids=$(ps aux | grep "[C]opilotVoice.dll" | awk '{print $2}')
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

# Build
echo "Building..."
"$DOTNET" build "$CSPROJ" -c Debug -v q 2>&1 | tail -3

# Copy to run dir (avoids rebuild-overwrites-running-binary crash)
rm -rf "$RUN_DIR"
mkdir -p "$RUN_DIR"
cp -r "$PROJECT_DIR/src/$APP_NAME/bin/Debug/net10.0/"* "$RUN_DIR/"

# Launch with watchdog (auto-restart on crash)
echo "Launching with watchdog..."
echo "" > "$LOG_FILE"

(
    while true; do
        cd "$RUN_DIR"
        "$DOTNET" "$APP_NAME.dll" >> "$LOG_FILE" 2>&1
        EXIT_CODE=$?
        echo "[watchdog] Process exited with code $EXIT_CODE at $(date). Restarting in 2s..." >> "$LOG_FILE"
        sleep 2
    done
) &
WATCHDOG_PID=$!
echo "$WATCHDOG_PID" > "$WATCHDOG_PID_FILE"

# Wait for app to start
sleep 4
APP_PID=$(ps aux | grep "[C]opilotVoice.dll" | awk '{print $2}' | head -1)
if [[ -n "$APP_PID" ]]; then
    echo "$APP_PID" > "$PID_FILE"
    echo "✅ Running (PID $APP_PID, watchdog $WATCHDOG_PID)"
    echo "Log: $LOG_FILE"
    tail -5 "$LOG_FILE"
else
    echo "❌ Failed to start — check $LOG_FILE"
    tail -20 "$LOG_FILE"
    exit 1
fi
