#!/bin/bash
set -euo pipefail

VERSION=${1:-"0.1.0"}
OUTPUT_DIR="publish"
DOTNET="dotnet"

# Check for dotnet
command -v dotnet &>/dev/null || DOTNET="/usr/local/share/dotnet/dotnet"

declare -A RIDS=(
    ["osx-arm64"]="copilot-voice-macos-arm64"
    ["osx-x64"]="copilot-voice-macos-x64"
    ["linux-x64"]="copilot-voice-linux-x64"
    ["win-x64"]="copilot-voice-windows-x64"
)

mkdir -p "$OUTPUT_DIR"

for rid in "${!RIDS[@]}"; do
    name="${RIDS[$rid]}"
    echo "Building $name..."
    $DOTNET publish src/CopilotVoice/CopilotVoice.csproj \
        -c Release \
        -r "$rid" \
        --self-contained true \
        /p:PublishSingleFile=true \
        /p:PublishTrimmed=true \
        -o "$OUTPUT_DIR/$name" \
        /p:Version="$VERSION"
done

echo "Done! Binaries in $OUTPUT_DIR/"
