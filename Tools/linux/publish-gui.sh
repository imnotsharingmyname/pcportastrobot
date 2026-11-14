#!/usr/bin/env bash
# Publish a self-contained Linux launcher under dist/astrohybrid-gui.
set -euo pipefail
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
PROJECT="$ROOT/AstroHybrid.App.Avalonia/AstroHybrid.App.Avalonia.csproj"
OUT="$ROOT/dist/astrohybrid-gui"

dotnet publish "$PROJECT" -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$OUT"
echo "published $OUT"
