#!/usr/bin/env bash
# Check out the pinned KytyPS5 revision and apply the hybrid series.
#
# Usage: Tools/linux/prepare-kyty.sh [stock|patched]
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
SRC=${KYTY_SRC:-"$ROOT/Tools/KytyPS5-jetsku"}
PIN=744705ee1df4b0acb1af382ff5c32ca4a20aa5f7
MODE=${1:-stock}

if [ ! -d "$SRC/.git" ]; then
  echo "== cloning KytyPS5 into $SRC"
  git clone https://github.com/Jetsku/KytyPS5.git "$SRC"
fi

cd "$SRC"
if [ -d .git/rebase-apply ]; then
  git am --abort || true
fi
git fetch --quiet origin
git checkout -f "$PIN"
git reset --hard "$PIN"
git submodule update --init --recursive --depth 1 -j 8

if [ "$MODE" = "patched" ]; then
  shopt -s nullglob
  patches=("$ROOT"/Source/kyty-hybrid/*.patch)
  for patch in "${patches[@]}"; do
    echo "== applying $(basename "$patch")"
    if ! git -c user.name=local -c user.email=local@localhost am --3way --keep-cr "$patch"; then
      echo "patch failed; inspect $SRC and run 'git am --abort' before retrying" >&2
      exit 1
    fi
  done
  linux_patches=("$ROOT"/Source/kyty-hybrid-linux/*.patch)
  for patch in "${linux_patches[@]}"; do
    echo "== applying $(basename "$patch")"
    if ! git -c user.name=local -c user.email=local@localhost am --3way --keep-cr "$patch"; then
      echo "Linux patch failed; inspect $SRC and run 'git am --abort' before retrying" >&2
      exit 1
    fi
  done
  echo "== Kyty patched tree: $(git rev-parse HEAD^{tree})"
elif [ "$MODE" != "stock" ]; then
  echo "unknown mode: $MODE (expected stock or patched)" >&2
  exit 2
fi

echo "== Kyty ready at $SRC ($(git rev-parse --short HEAD))"
