#!/usr/bin/env bash
# Configure and build KytyPS5 for Linux, then link the hybrid kyty_gpu.so.
#
# Kyty's Linux CI uses Clang; GCC 16 rejects a few Vulkan-Hpp initializer lists in
# this U59 fork, so Clang is the default here.
#
# Usage: Tools/linux/build-kyty.sh [build-dir]
#
# PGO + ThinLTO by default (the Windows release has both): with Tools/linux/pgo/*.proftext the
# build is ~20% faster on the command-processor thread (Sky Garden 38-39 vs 31-32 fps, 2026-11-24).
# Profiles are kept in llvm-profdata's text format, which any LLVM version reads; the build
# converts it with llvm-profdata (LLVM_PROFDATA, same LLVM version as the compiler).
#   KYTY_PGO=0          plain -O3 build
#   KYTY_PGO=generate   instrumented build; run it with KYTY_PGO_FILE=<file.profraw> (written every
#                       10 s, Tools/linux/pgo/pgo_dump.cpp), then
#                       llvm-profdata merge --text -o Tools/linux/pgo/<name>.proftext <file.profraw>
#   KYTY_PGO_PROFILE    profile to use (.proftext or .profdata; default: last Tools/linux/pgo/*.proftext by name)
# A profile matches the source it was recorded with; after big Kyty changes record a new one.
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

PGO_ARGS=(-DKYTY_PGO_GENERATE=OFF -DKYTY_PGO_USE= -DKYTY_EMULATOR_IPO=OFF -DCMAKE_EXE_LINKER_FLAGS=)
EXTRA_OBJS=
if [ "${KYTY_PGO:-1}" = generate ]; then
  PGO_ARGS=(-DKYTY_PGO_GENERATE=ON -DKYTY_PGO_USE= -DKYTY_EMULATOR_IPO=OFF -DCMAKE_EXE_LINKER_FLAGS=)
  mkdir -p "$BUILD"
  "$CXX" -c -fPIC -O2 "$ROOT/Tools/linux/pgo/pgo_dump.cpp" -o "$BUILD/pgo_dump.o"
  EXTRA_OBJS="$BUILD/pgo_dump.o"
elif [ "${KYTY_PGO:-1}" != 0 ]; then
  PROFILE=${KYTY_PGO_PROFILE:-$(ls "$ROOT"/Tools/linux/pgo/*.proftext 2>/dev/null | tail -1)}
  PROFDATA=${LLVM_PROFDATA:-llvm-profdata}
  if [ -n "$PROFILE" ] && [ "${PROFILE##*.}" = proftext ]; then
    # Indexed profiles are version-specific (an LLVM 23 file is unreadable by clang 18): convert here.
    # The output is named after the profile so a new profile changes the flags and rebuilds.
    mkdir -p "$BUILD"
    INDEXED="$BUILD/$(basename "${PROFILE%.*}").profdata"
    if command -v "$PROFDATA" >/dev/null && "$PROFDATA" merge -o "$INDEXED" "$PROFILE"; then
      PROFILE=$INDEXED
    else
      echo "warning: $PROFDATA could not convert $PROFILE; building without PGO" >&2
      PROFILE=
    fi
  fi
  if [ -n "$PROFILE" ]; then
    PGO_ARGS=(-DKYTY_PGO_GENERATE=OFF "-DKYTY_PGO_USE=$PROFILE" -DKYTY_EMULATOR_IPO=ON -DCMAKE_EXE_LINKER_FLAGS=-fuse-ld=lld)
    echo "== PGO + ThinLTO with $PROFILE"
  fi
fi

echo "== configuring Kyty in $BUILD ($CC)"
cmake -S "$SRC" -B "$BUILD" -G Ninja "${PGO_ARGS[@]}" \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_C_COMPILER="$CC" \
  -DCMAKE_CXX_COMPILER="$CXX" \
  -DCMAKE_POSITION_INDEPENDENT_CODE=OFF \
  -DCMAKE_C_FLAGS="-O3 -fPIC" \
  -DCMAKE_CXX_FLAGS="-O3 -fPIC" \
  -DKYTY_BUILD_LAUNCHER=OFF \
  -DKYTY_CP_DIAGNOSTICS=OFF \
  -DKYTY_BUILD_ORIGIN=Source \
  -DBUILD_TESTING=OFF

echo "== building the emulator objects"
cmake --build "$BUILD" --target kyty_emulator --parallel "$(nproc)"

echo "== linking kyty_gpu.so"
KYTY_EXTRA_OBJS=$EXTRA_OBJS python3 "$ROOT/Tools/linux/build-kyty-gpu.py" --build "$BUILD" --out "$ROOT/Build/kyty-gpu"

echo "== done: $ROOT/Build/kyty-gpu/kyty_gpu.so"
