#!/usr/bin/env bash
# Configure and build AnyPS5 for Linux, including the system .prx libraries.
#
# Usage: Tools/linux/build-anyps5.sh [build-dir]
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
SRC=${ANYPS5_SRC:-"$ROOT/Tools/AnyPS5"}
BUILD=${1:-"$ROOT/Build/anyps5"}
PYTHON=${PYTHON:-$(command -v python3)}

if [ ! -d "$SRC" ]; then
  echo "AnyPS5 is missing at $SRC; run Tools/linux/prepare-anyps5.sh first" >&2
  exit 1
fi

echo "== configuring AnyPS5 in $BUILD"
cmake -S "$SRC" -B "$BUILD" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_C_COMPILER=gcc \
  -DCMAKE_CXX_COMPILER=g++ \
  -DPython3_EXECUTABLE="$PYTHON"

echo "== building relinker and runtime"
cmake --build "$BUILD" --parallel

echo "== building system libraries"
cmake --build "$BUILD" --target libs --parallel

echo "== done:"
echo "  relinker: $BUILD/core/relinker/relinker"
echo "  libs:     $BUILD/core/libs/libs"
