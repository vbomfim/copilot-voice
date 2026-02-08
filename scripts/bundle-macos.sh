#!/usr/bin/env bash
# bundle-macos.sh — Create a macOS .app bundle from published output
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="/usr/local/share/dotnet/dotnet"
CSPROJ="$PROJECT_DIR/src/CopilotVoice/CopilotVoice.csproj"
APP_NAME="CopilotVoice"

# Detect architecture
if [[ "$(uname -m)" == "arm64" ]]; then
    RID="osx-arm64"
else
    RID="osx-x64"
fi

PUBLISH_DIR="/tmp/copilot-voice-publish"
APP_BUNDLE="/tmp/CopilotVoice.app"
INSTALL_DIR="/Applications/CopilotVoice.app"

echo "Publishing ($RID)..."
"$DOTNET" publish "$CSPROJ" -c Release -r "$RID" --self-contained \
    -p:PublishSingleFile=true -o "$PUBLISH_DIR" 2>&1 | tail -3

echo "Creating app bundle..."
rm -rf "$APP_BUNDLE"
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

# Copy Info.plist
cp "$PROJECT_DIR/src/$APP_NAME/Info.plist" "$APP_BUNDLE/Contents/"

# Copy executable and native libs
cp "$PUBLISH_DIR/$APP_NAME" "$APP_BUNDLE/Contents/MacOS/"
cp "$PUBLISH_DIR/"*.dylib "$APP_BUNDLE/Contents/MacOS/" 2>/dev/null || true
cp -r "$PUBLISH_DIR/Assets" "$APP_BUNDLE/Contents/MacOS/" 2>/dev/null || true

# Copy icon
cp "$PROJECT_DIR/src/$APP_NAME/Assets/CopilotVoice.icns" "$APP_BUNDLE/Contents/Resources/"

# Make executable
chmod +x "$APP_BUNDLE/Contents/MacOS/$APP_NAME"

echo "✅ Bundle created: $APP_BUNDLE"
echo "   Size: $(du -sh "$APP_BUNDLE" | cut -f1)"

# Offer to install
if [[ "${1:-}" == "--install" ]]; then
    echo "Installing to $INSTALL_DIR..."
    rm -rf "$INSTALL_DIR"
    cp -r "$APP_BUNDLE" "$INSTALL_DIR"
    echo "✅ Installed to $INSTALL_DIR"
    echo "   Launch from Spotlight or: open $INSTALL_DIR"
else
    echo "   To install: bash scripts/bundle-macos.sh --install"
    echo "   To run now: open $APP_BUNDLE"
fi
