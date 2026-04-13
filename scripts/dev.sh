#!/usr/bin/env bash
# dev.sh — Build and run as .app bundle for development.
# This ensures macOS microphone permission works from ANY terminal,
# because the .app bundle has its own NSMicrophoneUsageDescription.
#
# Usage: bash scripts/dev.sh [--stop]
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="/usr/local/share/dotnet/dotnet"
APP_NAME="CopilotVoice"
CSPROJ="$PROJECT_DIR/src/$APP_NAME/$APP_NAME.csproj"
PUBLISH_DIR="/tmp/copilot-voice-dev"
APP_BUNDLE="/tmp/CopilotVoice-dev.app"

# Detect architecture
if [[ "$(uname -m)" == "arm64" ]]; then
    RID="osx-arm64"
else
    RID="osx-x64"
fi

stop_app() {
    local pids
    pids=$(pgrep -f "$APP_NAME" 2>/dev/null || true)
    for p in $pids; do
        echo "Stopping PID $p..."
        kill "$p" 2>/dev/null || true
    done
    sleep 1
}

if [[ "${1:-}" == "--stop" ]]; then
    stop_app
    echo "Done."
    exit 0
fi

stop_app

echo "Building ($RID)..."
"$DOTNET" publish "$CSPROJ" -c Debug -r "$RID" --self-contained \
    -p:PublishSingleFile=false -o "$PUBLISH_DIR" 2>&1 | tail -3

echo "Creating dev .app bundle..."
rm -rf "$APP_BUNDLE"
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

# Info.plist with NSMicrophoneUsageDescription — this is the key
cp "$PROJECT_DIR/src/$APP_NAME/Info.plist" "$APP_BUNDLE/Contents/"

# Copy all published files
cp -R "$PUBLISH_DIR/"* "$APP_BUNDLE/Contents/MacOS/"
chmod +x "$APP_BUNDLE/Contents/MacOS/$APP_NAME"

# Copy icon if present
if [[ -f "$PROJECT_DIR/src/$APP_NAME/Assets/CopilotVoice.icns" ]]; then
    cp "$PROJECT_DIR/src/$APP_NAME/Assets/CopilotVoice.icns" "$APP_BUNDLE/Contents/Resources/"
fi

echo "Launching $APP_BUNDLE..."

# Source env vars (Azure keys etc.)
source ~/.zprofile 2>/dev/null || true

# Run in foreground so you see console output
open -W "$APP_BUNDLE" --stdout "$(tty)" --stderr "$(tty)" 2>/dev/null \
    || "$APP_BUNDLE/Contents/MacOS/$APP_NAME"
