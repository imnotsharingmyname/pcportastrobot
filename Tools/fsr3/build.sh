#!/usr/bin/env bash
# Build AMD's FSR 3.1 Vulkan provider for Linux.
#
#   FSR3_SDK_DIR=/path/to/FidelityFX-SDK-2.4.0-custom Tools/fsr3/build.sh
#
# Produces Build/fsr3/libamd_fidelityfx_fsr3_vk.so.
#
# Every permutation key is compiled on purpose. The SDK indexes the indirection
# table with the key it computed from the context flags; an entry that was never
# built cannot be reported to it, and a sentinel there is read as a blob and
# produces garbage descriptor counts. The full set is 10 passes x 64 keys.
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
SDK=${FSR3_SDK_DIR:-}
if [ -z "$SDK" ]; then
  echo "FSR3_SDK_DIR is not set; point it at the FidelityFX-SDK-2.4.0-custom tree" >&2
  exit 2
fi
SDK=$(cd "$SDK" && pwd)
DXC=${DXC:-$(command -v dxc || true)}
if [ -z "$DXC" ]; then
  echo "dxc not found; set DXC=/path/to/dxc" >&2
  exit 2
fi

SHADERS="$ROOT/Build/fsr3/shaders"
BUILD="$ROOT/Build/fsr3/build"

echo "== compiling shader tables into $SHADERS"
python3 "$ROOT/Tools/fsr3/build-shaders.py" \
  --sdk "$SDK" --dxc "$DXC" --out "$SHADERS" --kits upscaler --keys all

echo "== building the provider"
cmake -S "$ROOT/Tools/fsr3" -B "$BUILD" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DFSR3_SDK="$SDK" \
  -DSHADERS="$SHADERS"
cmake --build "$BUILD" --target amd_fidelityfx_fsr3_vk --parallel

mkdir -p "$ROOT/Build/fsr3"
cp "$BUILD/libamd_fidelityfx_fsr3_vk.so" "$ROOT/Build/fsr3/"

echo "== staged $ROOT/Build/fsr3/libamd_fidelityfx_fsr3_vk.so"
echo "   smoke test: FSR3_PROVIDER=$ROOT/Build/fsr3/libamd_fidelityfx_fsr3_vk.so \\"
echo "               $ROOT/Build/fsr3/context_smoke"
