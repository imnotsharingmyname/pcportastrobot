#!/usr/bin/env bash
# Configure and build KytyPS5 for Linux, then link the hybrid kyty_gpu.so.
#
# Kyty's Linux CI uses Clang; GCC 16 rejects a few Vulkan-Hpp initializer lists in
# this U59 fork, so Clang is the default here.
#
# Usage: Tools/linux/build-kyty.sh [build-dir]
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
SRC=${KYTY_SRC:-"$ROOT/Tools/KytyPS5-jetsku"}
BUILD=${1:-"$ROOT/Build/kyty-clang"}
CC=${CC:-clang}
CXX=${CXX:-clang++}

if [ ! -d "$SRC" ]; then
  echo "Kyty is missing at $SRC; run Tools/linux/prepare-kyty.sh first" >&2
  exit 1
fi

echo "== configuring Kyty in $BUILD ($CC)"
cmake -S "$SRC" -B "$BUILD" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_C_COMPILER="$CC" \
  -DCMAKE_CXX_COMPILER="$CXX" \
  -DCMAKE_POSITION_INDEPENDENT_CODE=OFF \
  -DCMAKE_C_FLAGS=-fPIC \
  -DCMAKE_CXX_FLAGS=-fPIC \
  -DBUILD_TESTING=OFF

echo "== building the emulator objects"
cmake --build "$BUILD" --target kyty_emulator --parallel "$(nproc)"

echo "== linking kyty_gpu.so"
python3 "$ROOT/Tools/linux/build-kyty-gpu.py" --build "$BUILD" --out "$ROOT/Build/kyty-gpu"

echo "== done: $ROOT/Build/kyty-gpu/kyty_gpu.so"
