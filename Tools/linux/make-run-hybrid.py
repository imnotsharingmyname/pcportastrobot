#!/usr/bin/env python3
"""Lay out Build/RunHybrid: the relinked title with Kyty's GPU stack.

Mirrors Tools/hybrid/make_run_hybrid.py on Windows.  The stock Build/Run
executable is reused; its graphics libraries are replaced by kyty_gpu.so and
the patched AnyPS5 kernel/libc/pad libraries.
"""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUN = ROOT / "Build" / "Run"
HYBRID = ROOT / "Build" / "RunHybrid"
PATCHED_LIBS = ROOT / "Build" / "anyps5-hybrid" / "core" / "libs" / "libs"
KYTY_GPU = ROOT / "Build" / "kyty-gpu" / "kyty_gpu.so"

FRESH = ("libc.prx", "libkernel.prx", "libScePad.prx")
SHIMS = ("libSceAgc.prx", "libSceAgcDriver.prx", "libSceVideoOut.prx")
HOST = "libAstroHybridHost.prx"


def fail(message: str) -> "NoReturn":
    raise SystemExit(message)


def relink_or_copy(source: Path, target: Path) -> None:
    if target.is_symlink() or target.is_file():
        target.unlink()
    elif target.is_dir():
        shutil.rmtree(target)
    if source.is_dir():
        target.symlink_to(source, target_is_directory=True)
    else:
        target.symlink_to(source)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run", default=str(RUN))
    parser.add_argument("--out", default=str(HYBRID))
    parser.add_argument("--kyty-gpu", default=str(KYTY_GPU))
    args = parser.parse_args()

    run = Path(args.run).resolve()
    hybrid = Path(args.out).resolve()
    kyty_gpu = Path(args.kyty_gpu).resolve()

    binary = run / "AstroBot"
    if not binary.is_file():
        fail(f"{binary} is missing; run astrohybrid.py setup first")
    for required in (PATCHED_LIBS, kyty_gpu):
        if not required.exists():
            fail(f"missing {required}")

    hybrid.mkdir(parents=True, exist_ok=True)
    shutil.copy2(binary, hybrid / "AstroBot")
    subprocess.run([sys.executable, str(ROOT / "Tools" / "linux" / "reorder-needed.py"),
                    str(hybrid / "AstroBot")], check=True)
    for item in run.iterdir():
        if item.is_file() and item.name.startswith("AstroBot") and item.suffix == ".json":
            shutil.copy2(item, hybrid / item.name)

    libs = hybrid / "libs"
    if libs.exists():
        shutil.rmtree(libs)
    libs.mkdir(parents=True)

    replaced = set(FRESH) | set(SHIMS)
    linked = 0
    for item in (run / "libs").iterdir():
        if item.is_file() and item.name not in replaced:
            shutil.copy2(item, libs / item.name)
            linked += 1

    for name in FRESH:
        source = PATCHED_LIBS / name
        if not source.is_file():
            fail(f"missing patched library {source}")
        shutil.copy2(source, libs / name)

    shutil.copy2(PATCHED_LIBS / HOST, libs / HOST)
    shutil.copy2(kyty_gpu, libs / "kyty_gpu.so")
    for name in SHIMS:
        (libs / name).symlink_to("kyty_gpu.so")

    app0 = hybrid / "app0"
    if not app0.exists() and not app0.is_symlink():
        relink_or_copy(run / "app0", app0)

    for directory in ("_sd_mem", "_trophy"):
        source = run / directory
        target = hybrid / directory
        if not target.exists():
            if source.exists():
                shutil.copytree(source, target)
            else:
                target.mkdir()

    print(f"RunHybrid: {linked} stock prx, fresh {', '.join(FRESH)}, "
          f"{len(SHIMS)} graphics shims, kyty_gpu.so from {kyty_gpu}")
    print(f"runtime: {hybrid}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
