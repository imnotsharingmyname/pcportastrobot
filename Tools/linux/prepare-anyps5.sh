#!/usr/bin/env bash
# Check out the pinned AnyPS5 revision and optionally apply this repository's series.
#
# Usage:
#   Tools/linux/prepare-anyps5.sh [stock|patched]
#
#   stock    upstream 319621b only (baseline)
#   patched  + Source/anyps5/*.patch (AstroHybrid host and memory hooks)
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
SRC=${ANYPS5_SRC:-"$ROOT/Tools/AnyPS5"}
PIN=319621ba2e578bd053b0baba2877fe70520e0737
MODE=${1:-stock}

if [ ! -d "$SRC/.git" ]; then
  echo "== cloning AnyPS5 into $SRC"
  git clone https://github.com/boykopovar/AnyPS5.git "$SRC"
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
  patches=("$ROOT"/Source/anyps5/*.patch)
  if [ ${#patches[@]} -eq 0 ]; then
    echo "no patches in $ROOT/Source/anyps5" >&2
    exit 1
  fi
  for patch in "${patches[@]}"; do
    echo "== applying $(basename "$patch")"
    if ! git -c user.name=local -c user.email=local@localhost am --3way --keep-cr "$patch"; then
      echo "patch failed; inspect $SRC and run 'git am --abort' before retrying" >&2
      exit 1
    fi
  done
  shopt -s nullglob
  linux_patches=("$ROOT"/Source/anyps5-linux/*.patch)
  for patch in "${linux_patches[@]}"; do
    echo "== applying $(basename "$patch")"
    if ! git -c user.name=local -c user.email=local@localhost am --3way --keep-cr "$patch"; then
      echo "Linux patch failed; inspect $SRC and run 'git am --abort' before retrying" >&2
      exit 1
    fi
  done
  echo "== AnyPS5 patched tree: $(git rev-parse HEAD^{tree})"
elif [ "$MODE" != "stock" ]; then
  echo "unknown mode: $MODE (expected stock or patched)" >&2
  exit 2
fi

echo "== AnyPS5 ready at $SRC ($(git rev-parse --short HEAD))"
