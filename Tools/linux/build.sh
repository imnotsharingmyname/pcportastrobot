#!/usr/bin/env bash
# Build the Linux runtime.
#
# Usage:
#   Tools/linux/build.sh stock    AnyPS5 only (baseline)
#   Tools/linux/build.sh hybrid   AnyPS5 host hooks + Kyty GPU bridge (later stage)
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
MODE=${1:-stock}

case "$MODE" in
  stock)
    "$ROOT/Tools/linux/prepare-anyps5.sh" stock
    "$ROOT/Tools/linux/build-anyps5.sh"
    ;;
  hybrid)
    "$ROOT/Tools/linux/prepare-anyps5.sh" patched
    "$ROOT/Tools/linux/build-anyps5.sh" "$ROOT/Build/anyps5-hybrid"
    "$ROOT/Tools/linux/prepare-kyty.sh" patched
    "$ROOT/Tools/linux/build-kyty.sh"
    if [ -d "$ROOT/Game/PPSA21567-app" ]; then
      "$ROOT/Tools/linux/run.sh" setup --game "$ROOT/Game/PPSA21567-app"
      "$ROOT/Tools/linux/run.sh" setup --hybrid --game "$ROOT/Game/PPSA21567-app"
    else
      echo "No Game/PPSA21567-app dump found; run Tools/linux/run.sh setup after extracting your dump."
    fi
    ;;
  *)
    echo "usage: $0 [stock|hybrid]" >&2
    exit 2
    ;;
esac

echo "== build complete ($MODE)"
