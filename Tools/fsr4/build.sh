#!/usr/bin/env bash
# Build the native FSR 4.1.2 FidelityFX Vulkan provider and stage it under Build/fsr4.
#
# The provider is the GPL-3.0 Linux port in the user's FidelityFX SDK custom tree.
# Its shaders/model banks are extracted from the user's signed AMD DLL and are not
# committed to this repository.
#
# Usage:
#   FSR4_SDK_DIR=/path/to/FidelityFX-SDK-2.4.0-custom Tools/fsr4/build.sh
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
SDK=${FSR4_SDK_DIR:-}
if [ -z "$SDK" ]; then
  echo "FSR4_SDK_DIR is not set; point it at the FidelityFX-SDK-2.4.0-custom tree" >&2
  exit 2
fi
SDK=$(cd "$SDK" && pwd)
if [ ! -d "$SDK/linux" ]; then
  echo "$SDK/linux is missing; this is not the Linux FSR4 SDK tree" >&2
  exit 2
fi

BUILD="$SDK/linux/build/native"
ASSETS="$SDK/linux/build/assets"
if [ ! -d "$ASSETS" ]; then
  echo "== extracting FSR4 assets from the signed DLL"
  python3 "$SDK/linux/tools/extract_all.py"
fi

echo "== configuring/building the FSR4 provider"
cmake -S "$SDK/linux" -B "$BUILD" -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build "$BUILD" --target amd_fidelityfx_upscaler_vk amd_fidelityfx_loader_vk --parallel

mkdir -p "$ROOT/Build/fsr4"
cp "$BUILD/libamd_fidelityfx_upscaler_vk.so" "$BUILD/libamd_fidelityfx_loader_vk.so" "$ROOT/Build/fsr4/"
ln -sfn "$ASSETS" "$ROOT/Build/fsr4/assets"

echo "== staged $ROOT/Build/fsr4"
echo "   provider test: $BUILD/fsr4_provider_test"
