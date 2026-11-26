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
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

GENERATED_VARIANTS = (
    "AstroBot-NonRT",
    "AstroBot-Ultrawide",
    "AstroBot-NonRT-Ultrawide",
    "AstroBot-NonRT-SoftAssert",
    "AstroBot-NonRT-Ultrawide-SoftAssert",
)

# (name, original hex, patched hex) ported from AstroHybrid.App/Services/GameSetup.cs.
SOFT_ASSERT = (
    "soft command-buffer assert",
    "554889e5488d3ded6fbb07488d0dc47fc2074c8d052299e407be1f000000ba0100000031c0e8660eb6ff85c07402cd42488d3d3c54d20731c0e88235fbffcd4131c05dc3",
    "554889e5eb2a3ded6fbb07488d0dc47fc2074c8d052299e407be1f000000ba0100000031c0e8660eb6ff85c07402cd42488d3d3c54d20731c0e88235fbff909031c05dc3",
)

# Astro Bot's internal render aspect constant: 16:9 -> 21:9-class (3440x1440).
# All occurrences are patched, as the Windows setup does.
ULTRAWIDE = ("ultrawide render aspect", "398ee33f", "8ee31840")


LIGHTING = [
    ("non-tiled lighting", "4584f60f84f10800004531f64c8d3d", "4584f6e9f2080000904531f64c8d3d"),
    ("lighting shaders off", "0fb682a00700008887e4050000", "b80000000090908887e4050000"),
]
CMDBUF_TABLE_16 = "1000000010000000100000001000000010000000100020003000400008000000"
BASE_PATCHES = [
    ("cmdbuf_worker_mib_table", "0700000007000000040000000400000006000000100020003000400008000000", CMDBUF_TABLE_16),
    ("cmdbuf_worker_mib_table_16", "0800000008000000080000000800000008000000100020003000400008000000", CMDBUF_TABLE_16),
    ("cmdbuf_null_memcpy_guard1", "4531FFEB0F49C1E4024D29E74983E7FC4D897E18488B7318", "4531FFEB2649C1E4024D29E74983E7FC4D897E18488B7318"),
    ("cmdbuf_null_memcpy_guard2", "4531FFEB0F49C1E4024D29E74983E7FC4D897E18488B7328", "4531FFEB5849C1E4024D29E74983E7FC4D897E18488B7328"),
    ("cmdbuf_null_memcpy_guard3", "4531FFEB134A8D04A5000000004929C74983E7FC4D897E18488B7320", "4531FFEB634A8D04A5000000004929C74983E7FC4D897E18488B7320"),
    ("garlic_pool_plus_256mib", "B80000C08748C785", "B80000C09748C785"),
]


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
    # Optional TOML loader requires Python 3.11; normal setup does not call it.
    import tomllib

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


def write_variant(run: Path, base: bytes | bytearray, patches, label: str, written: list[str]) -> bytearray | None:
    buffer = bytearray(base)
    for name, original, patched in patches:
        count = apply_all(buffer, name, original, patched)
        if count == 0:
            log(f"==   {label}: '{name}' did not match; variant skipped")
            return None
    target = run / f"AstroBot-{label}"
    target.write_bytes(buffer)
    target.chmod(0o755)
    written.append(target.name)
    log(f"==   wrote {target.name}")
    return buffer


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

    base = bytearray(base_path.read_bytes())
    # Same base patches as the Windows setup (AstroHybrid.App/Services/GameSetup.cs): 16 MiB worker
    # command buffers, NULL-memcpy guards (#47, #50, #6, #159) and the Garlic pool +256 MiB (#166).
    base_changed = False
    for name, original, patched in BASE_PATCHES:
        count = apply_all(base, name, bytes.fromhex(original), bytes.fromhex(patched))
        base_changed |= count > 0
        log(f"==   {name}: {count} match(es)")
    if base_changed:
        base_path.write_bytes(base)
    # The toml patterns are the bare 6-byte JE (17 matches in 01.019.000: patching all of them breaks
    # unrelated branches, e.g. an empty std::function throw at ps_logo). Use the Windows setup's unique
    # patterns (GameSetup.cs FasterLightingPatches) and require exactly one match.
    lighting = [(name, bytes.fromhex(o), bytes.fromhex(n)) for name, o, n in LIGHTING]
    for name, original, _ in lighting:
        if base.count(original) != 1:
            log(f"==   faster lighting: '{name}' found {base.count(original)} times; variant skipped")
            lighting = []
            break
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

    # Generate the plain ultrawide variant before modifying the base buffer for faster lighting.
    # Only two executable buffers are needed at this point.
    ultra_patch = [(ULTRAWIDE[0], bytes.fromhex(ULTRAWIDE[1]), bytes.fromhex(ULTRAWIDE[2]))]
    if write_variant(run, base, ultra_patch, "Ultrawide", written) is not None:
        features["ultrawide"] = True

    # Faster lighting (non-tiled renderer + GI probes off).
    if lighting:
        nonrt = base  # no later variant needs the unmodified base buffer
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

    if "AstroBot-NonRT" in written:
        soft_patch = [(SOFT_ASSERT[0], bytes.fromhex(SOFT_ASSERT[1]), bytes.fromhex(SOFT_ASSERT[2]))]
        if write_variant(run, nonrt, soft_patch, "NonRT-SoftAssert", written) is not None:
            features["softAssert"] = True
        nonrt_ultra = write_variant(run, nonrt, ultra_patch, "NonRT-Ultrawide", written)
        del nonrt, base
        if nonrt_ultra is not None:
            write_variant(run, nonrt_ultra, soft_patch, "NonRT-Ultrawide-SoftAssert", written)

    # The launcher selects variants by file existence. A previous installation
    # must not leave an old executable selectable if its signature no longer matches.
    for name in GENERATED_VARIANTS:
        if name not in written:
            (run / name).unlink(missing_ok=True)

    features["variants"] = written
    manifest = run / "features.json"
    manifest.write_text(json.dumps(features, indent=2) + "\n", encoding="utf-8")
    log(f"== variants written: {', '.join(written)}")
    log(f"== manifest: {manifest}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
