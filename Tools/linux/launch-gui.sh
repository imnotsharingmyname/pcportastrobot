#!/usr/bin/env bash
# Build (if needed) and start the Avalonia Linux launcher.
set -euo pipefail
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
PROJECT="$ROOT/AstroHybrid.App.Avalonia/AstroHybrid.App.Avalonia.csproj"
OUTPUT="$ROOT/AstroHybrid.App.Avalonia/bin/Release/net8.0/AstroHybrid.dll"

if [ ! -f "$OUTPUT" ]; then
  dotnet build "$PROJECT" -c Release
fi
exec dotnet "$OUTPUT" "$@"
