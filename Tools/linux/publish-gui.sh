#!/usr/bin/env bash
# Publish a self-contained Linux launcher under dist/astrohybrid-gui.
set -euo pipefail
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
PROJECT="$ROOT/AstroHybrid.App.Avalonia/AstroHybrid.App.Avalonia.csproj"
OUT="$ROOT/dist/astrohybrid-gui"

version_args=()
if [[ -n "${ASTROHYBRID_PACKAGE_VERSION:-}" ]]; then
  version_args+=("-p:Version=$ASTROHYBRID_PACKAGE_VERSION")
fi

dotnet publish "$PROJECT" "${version_args[@]}" -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$OUT"
echo "published $OUT"
