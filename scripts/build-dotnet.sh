#!/usr/bin/env bash
# Build single-file Windows binaries for the C# port of LPC Sprite Generator.
# Output: bins/lpc-sprite-generator-dotnet/{headless,wpf}/
#
# Run from anywhere — it locates the dotnet/ solution relative to its own path.
set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
SOLUTION="$SCRIPT_DIR/../dotnet/LpcSpriteGen.sln"

echo "=== Restoring + building solution (Release) ==="
dotnet build "$SOLUTION" -c Release

echo
echo "=== Publishing headless CLI (single-file, win-x64) ==="
dotnet publish "$SCRIPT_DIR/../dotnet/src/LpcSpriteGen.Headless/LpcSpriteGen.Headless.csproj" \
    /p:PublishProfile=win-x64.pubxml

echo
echo "=== Publishing WPF GUI (single-file, win-x64) ==="
dotnet publish "$SCRIPT_DIR/../dotnet/src/LpcSpriteGen.Wpf/LpcSpriteGen.Wpf.csproj" \
    /p:PublishProfile=win-x64.pubxml

echo
echo "=== Done. Binaries in bins/lpc-sprite-generator-dotnet/ ==="
