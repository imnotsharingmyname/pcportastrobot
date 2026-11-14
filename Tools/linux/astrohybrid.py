#!/usr/bin/env python3
"""Linux setup and launch helper for the Astro Bot native port.

The stock mode converts the title with AnyPS5's own GPU stack.  Hybrid mode is
added by the Kyty bridge work; this file owns the game-folder validation,
SELF unwrapping, relinking and runtime layout so the GUI can stay thin.
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BUILD = ROOT / "Build"
ANYPS5 = ROOT / "Tools" / "AnyPS5"
RUN = BUILD / "Run"
RUN_HYBRID = BUILD / "RunHybrid"
ELF_INPUT = BUILD / "elf-input"
GAME = ROOT / "Game" / "PPSA21567-app"
TITLE_ID = "PPSA21567"
TESTED_VERSION = "01.019.000"


def log(message: str) -> None:
    print(message, flush=True)


def fail(message: str) -> "NoReturn":
    raise SystemExit(message)


def find_relinker() -> Path:
    for candidate in (BUILD / "anyps5-hybrid" / "core" / "relinker" / "relinker",
                      BUILD / "anyps5" / "core" / "relinker" / "relinker"):
        if candidate.is_file():
            return candidate
    fail(f"relinker not found under {BUILD}; run Tools/linux/build-anyps5.sh first")


def find_libs() -> Path:
    for libs in (BUILD / "anyps5-hybrid" / "core" / "libs" / "libs",
                 BUILD / "anyps5" / "core" / "libs" / "libs"):
        if libs.is_dir():
            return libs
    fail(f"system libraries not found under {BUILD}; run Tools/linux/build-anyps5.sh first")


def host_needs_to_intel() -> bool:
    """The relinker lowers AMD-only instructions (SSE4A, CLZERO) when asked."""
    try:
        text = Path("/proc/cpuinfo").read_text(encoding="utf-8", errors="replace")
    except OSError:
        return True
    for line in text.splitlines():
        if line.startswith("flags"):
            flags = set(line.split(":", 1)[1].split())
            return not {"sse4a", "clzero"}.issubset(flags)
    return True


def read_param(game: Path) -> dict:
    param = game / "sce_sys" / "param.json"
    if not param.is_file():
        fail(f"{param} is missing: this is not a decrypted PPSA21567 dump")
    try:
        data = json.loads(param.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        fail(f"cannot read {param}: {exc}")
    if data.get("titleId") != TITLE_ID:
        fail(f"{param}: titleId is {data.get('titleId')!r}, expected {TITLE_ID!r}")
    return data


def validate_game(game: Path) -> dict:
    game = game.expanduser().resolve()
    if not game.is_dir():
        fail(f"game folder not found: {game}")
    for required in ("eboot.bin", "sce_module", "sce_sys"):
        if not (game / required).exists():
            fail(f"{game} has no {required}; point --game at the extracted PPSA21567-app folder")
    param = read_param(game)
    log(f"== {TITLE_ID} {param.get('contentVersion', 'unknown version')} at {game}")
    return param


def unwrap(game: Path) -> None:
    unwrap_tool = ANYPS5 / "tools" / "unwrap_self.py"
    if not unwrap_tool.is_file():
        fail(f"missing {unwrap_tool}; run Tools/linux/prepare-anyps5.sh")
    ELF_INPUT.mkdir(parents=True, exist_ok=True)
    modules = ELF_INPUT / "sce_module"
    modules.mkdir(parents=True, exist_ok=True)

    log("== unwrapping eboot.bin")
    subprocess.run([sys.executable, "-I", str(unwrap_tool), str(game / "eboot.bin"),
                    str(ELF_INPUT / "eboot.elf")], check=True)

    originals = sorted((game / "sce_module").glob("*.prx"))
    for source in originals:
        if source.name.endswith(".guest.prx"):
            continue
        target = modules / source.name
        log(f"== unwrapping sce_module/{source.name}")
        subprocess.run([sys.executable, "-I", str(unwrap_tool), str(source), str(target)],
                       check=True)


def link_or_copy(source: Path, target: Path) -> None:
    if target.is_symlink() or target.is_file():
        target.unlink(missing_ok=True)
    elif target.is_dir():
        shutil.rmtree(target)
    if source.is_dir():
        target.symlink_to(source, target_is_directory=True)
    else:
        target.symlink_to(source)


def make_app0(game: Path, run: Path) -> None:
    """Give the title an app0 that does not write converted modules into the dump."""
    app0 = run / "app0"
    app0.mkdir(parents=True, exist_ok=True)

    for entry in game.iterdir():
        target = app0 / entry.name
        if entry.name == "sce_module":
            continue  # copied below so the relinker can write the guest modules here
        if entry.name == "sce_sys":
            if target.exists() or target.is_symlink():
                if target.is_dir() and not target.is_symlink():
                    shutil.rmtree(target)
                else:
                    target.unlink()
            shutil.copytree(entry, target)
            continue
        link_or_copy(entry, target)

    modules = app0 / "sce_module"
    if modules.exists() or modules.is_symlink():
        if modules.is_dir() and not modules.is_symlink():
            shutil.rmtree(modules)
        else:
            modules.unlink()
    shutil.copytree(game / "sce_module", modules)


def relink(game: Path, run: Path, hybrid: bool) -> None:
    relinker = find_relinker()
    libs = find_libs()
    run.mkdir(parents=True, exist_ok=True)
    make_app0(game, run)

    target = run / "AstroBot"
    command = [str(relinker), "--registry"]
    if host_needs_to_intel():
        log("== host lacks SSE4A/CLZERO; lowering AMD-only instructions (--to-intel)")
        command.append("--to-intel")
    command += [str(ELF_INPUT / "eboot.elf"), str(target)]
    log(f"== relinking to {target}")
    subprocess.run(command, check=True)

    out_libs = run / "libs"
    out_libs.mkdir(parents=True, exist_ok=True)
    for item in libs.iterdir():
        if item.is_file() and item.suffix in (".prx", ".so"):
            shutil.copy2(item, out_libs / item.name)

    for directory in ("_sd_mem", "_trophy"):
        (run / directory).mkdir(exist_ok=True)

    if not hybrid:
        log("== stock AnyPS5 runtime laid out; pass --hybrid after the Kyty bridge is built")


def cmd_check(args: argparse.Namespace) -> int:
    validate_game(Path(args.game))
    find_relinker()
    find_libs()
    log(f"== relinker: {find_relinker()}")
    log(f"== libs:     {find_libs()}")
    return 0


def cmd_setup(args: argparse.Namespace) -> int:
    game = Path(args.game).expanduser().resolve()
    param = validate_game(game)
    if args.hybrid:
        if not (RUN / "AstroBot").is_file():
            fail("Build/Run/AstroBot is missing; run 'setup' without --hybrid first")
        subprocess.run([sys.executable, str(ROOT / "Tools" / "linux" / "make-run-hybrid.py")],
                       check=True)
    else:
        unwrap(game)
        relink(game, RUN, False)
    log(f"== done ({param.get('contentVersion', 'unknown')})")
    return 0


def cmd_run(args: argparse.Namespace) -> int:
    run = RUN_HYBRID if args.hybrid else RUN
    binary = run / "AstroBot"
    if not binary.is_file():
        fail(f"{binary} is missing; run 'astrohybrid.py setup' first")
    if args.direct_audio:
        helper = ROOT / "Tools" / "linux" / "route-audio.py"
        if helper.is_file():
            command = [sys.executable, str(helper), "--quiet"]
            if args.audio_sink:
                command += ["--sink", args.audio_sink]
            try:
                subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                 start_new_session=True)
            except OSError:
                pass
    env = os.environ.copy()
    if args.hybrid:
        env.setdefault("APS5_GPU_ENGINE", "kyty")
        env.setdefault("APS5_HOST_IMPORT_MIB", "11264")
        env.setdefault("KYTY_GPU_HOST_MODULE", "libs/libAstroHybridHost.prx")
        env.setdefault("KYTY_GPU_WIDTH", "1920")
        env.setdefault("KYTY_GPU_HEIGHT", "1080")
    for name, value in args.env:
        env[name] = value
    log(f"== launching {binary}" + (" (hybrid)" if args.hybrid else ""))
    os.chdir(run)
    os.execve(binary, [str(binary), *args.extra], env)


def add_common(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--game", default=str(GAME), help="extracted PPSA21567-app folder")
    parser.add_argument("--hybrid", action="store_true", help="use the Kyty bridge runtime")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)

    p_check = sub.add_parser("check", help="validate the dump and build outputs")
    add_common(p_check)
    p_check.set_defaults(func=cmd_check)

    p_setup = sub.add_parser("setup", help="unwrap and relink the title")
    add_common(p_setup)
    p_setup.set_defaults(func=cmd_setup)

    p_run = sub.add_parser("run", help="launch the converted title")
    add_common(p_run)
    p_run.add_argument("--env", nargs=2, action="append", default=[], metavar=("NAME", "VALUE"))
    p_run.add_argument("--direct-audio", action="store_true",
                       help="route the game's stream to the default sink, bypassing filter chains")
    p_run.add_argument("--audio-sink", default="", help="PipeWire sink name for --direct-audio")
    p_run.add_argument("extra", nargs=argparse.REMAINDER)
    p_run.set_defaults(func=cmd_run)

    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
