#!/usr/bin/env python3
"""Apply the Astro Bot game patches and write the runtime variants.

The Windows launcher applies byte patches to the relinked AstroBot.exe and
ships one executable per combination. This does the same to the relinked Linux
ELF, so the launcher can select a variant instead of exposing dead switches.

Variants (only those whose patches matched are written):
  AstroBot                          base
  AstroBot-NonRT                    faster lighting (non-tiled renderer)
  AstroBot-Ultrawide                21:9 render aspect
  AstroBot-NonRT-Ultrawide
  AstroBot-NonRT-SoftAssert         soft command-buffer assert
  AstroBot-NonRT-Ultrawide-SoftAssert

Usage: patch_game.py --run Build/RunHybrid [--patches Source/game-patches]
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import sys
import tomllib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

# (name, original hex, patched hex) ported from AstroHybrid.App/Services/GameSetup.cs.
SOFT_ASSERT = (
    "soft command-buffer assert",
    "554889e5488d3ded6fbb07488d0dc47fc2074c8d052299e407be1f000000ba0100000031c0e8660eb6ff85c07402cd42488d3d3c54d20731c0e88235fbffcd4131c05dc3",
    "554889e5eb2a3ded6fbb07488d0dc47fc2074c8d052299e407be1f000000ba0100000031c0e8660eb6ff85c07402cd42488d3d3c54d20731c0e88235fbff909031c05dc3",
)

# Astro Bot's internal render aspect constant: 16:9 -> 21:9-class (3440x1440).
# All occurrences are patched, as the Windows setup does.
ULTRAWIDE = ("ultrawide render aspect", "398ee33f", "8ee31840")


def log(message: str) -> None:
    print(message, flush=True)


def apply_all(buffer: bytearray, name: str, original: bytes, patched: bytes) -> int:
    """Replace every occurrence; return how many were replaced."""
    if len(original) != len(patched):
        raise SystemExit(f"{name}: original and patched lengths differ")
    count = 0
    start = 0
    while True:
        index = buffer.find(original, start)
        if index < 0:
            break
        buffer[index:index + len(original)] = patched
        count += 1
        start = index + len(patched)
    return count


def load_toml_patches(directory: Path, names: list[str]) -> list[tuple[str, bytes, bytes]]:
    patches: list[tuple[str, bytes, bytes]] = []
    for name in names:
        path = directory / f"{name}.toml"
        if not path.is_file():
            continue
        with path.open("rb") as handle:
            data = tomllib.load(handle)
        for entry in data.get("patch", []):
            original = bytes.fromhex(entry["expected"])
            patched = bytes.fromhex(entry["value"])
            if len(original) != len(patched):
                raise SystemExit(f"{path.name}: {entry.get('name')}: length mismatch")
            patches.append((entry.get("name", path.stem), original, patched))
    return patches


def write_variant(run: Path, base: bytes, patches, label: str, written: list[str]) -> bool:
    buffer = bytearray(base)
    for name, original, patched in patches:
        count = apply_all(buffer, name, original, patched)
        if count == 0:
            log(f"==   {label}: '{name}' did not match; variant skipped")
            return False
    target = run / f"AstroBot-{label}"
    target.write_bytes(buffer)
    target.chmod(0o755)
    written.append(target.name)
    log(f"==   wrote {target.name}")
    return True


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run", default=str(ROOT / "Build" / "RunHybrid"))
    parser.add_argument("--patches", default=str(ROOT / "Source" / "game-patches"))
    parser.add_argument("--game", default=str(ROOT / "Game" / "PPSA21567-app"))
    args = parser.parse_args(argv)

    run = Path(args.run).expanduser().resolve()
    base_path = run / "AstroBot"
    if not base_path.is_file():
        raise SystemExit(f"{base_path} is missing; run setup first")

    base = base_path.read_bytes()
    lighting = load_toml_patches(Path(args.patches), ["non_tiled_lighting", "gi_probes_off"])
    if not lighting:
        log("== no lighting patches found; only the plain variants will be written")

    written: list[str] = ["AstroBot"]
    features = {
        "titleId": "PPSA21567",
        "version": "",
        "setupAt": dt.datetime.now().isoformat(timespec="seconds"),
        "nonTiledLighting": False,
        "ultrawide": False,
        "softAssert": False,
        "variants": written,
    }

    param = Path(args.game).expanduser() / "sce_sys" / "param.json"
    if param.is_file():
        try:
            features["version"] = json.loads(param.read_text()).get("contentVersion", "")
            features["titleId"] = json.loads(param.read_text()).get("titleId", "PPSA21567")
        except (OSError, ValueError):
            pass

    # Faster lighting (non-tiled renderer + GI probes off).
    if lighting:
        nonrt = bytearray(base)
        ok = True
        for name, original, patched in lighting:
            if apply_all(nonrt, name, original, patched) == 0:
                log(f"==   faster lighting: '{name}' did not match; variant skipped")
                ok = False
                break
        if ok:
            (run / "AstroBot-NonRT").write_bytes(nonrt)
            (run / "AstroBot-NonRT").chmod(0o755)
            written.append("AstroBot-NonRT")
            features["nonTiledLighting"] = True
            log("==   wrote AstroBot-NonRT")

    # Ultrawide, on the base and on the faster-lighting build.
    ultra_patch = [(ULTRAWIDE[0], bytes.fromhex(ULTRAWIDE[1]), bytes.fromhex(ULTRAWIDE[2]))]
    if write_variant(run, base, ultra_patch, "Ultrawide", written):
        features["ultrawide"] = True

    if (run / "AstroBot-NonRT").is_file():
        nonrt = (run / "AstroBot-NonRT").read_bytes()
        write_variant(run, nonrt, ultra_patch, "NonRT-Ultrawide", written)

        soft_patch = [(SOFT_ASSERT[0], bytes.fromhex(SOFT_ASSERT[1]), bytes.fromhex(SOFT_ASSERT[2]))]
        if write_variant(run, nonrt, soft_patch, "NonRT-SoftAssert", written):
            features["softAssert"] = True

        if (run / "AstroBot-NonRT-Ultrawide").is_file():
            nonrt_ultra = (run / "AstroBot-NonRT-Ultrawide").read_bytes()
            write_variant(run, nonrt_ultra, soft_patch, "NonRT-Ultrawide-SoftAssert", written)

    features["variants"] = written
    manifest = run / "features.json"
    manifest.write_text(json.dumps(features, indent=2) + "\n", encoding="utf-8")
    log(f"== variants written: {', '.join(written)}")
    log(f"== manifest: {manifest}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
