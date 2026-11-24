"""Assemble Build\\RunHybrid: AstroBot.exe with Kyty's GPU stack (AstroHybrid, milestone 2).

Build\\Run stays the working AnyPS5 build and is only read. RunHybrid gets:
  AstroBot.exe and its registries        hard links to Build\\Run (same files, no copies)
  libs\\*.prx (all others)                hard links to Build\\Run\\libs
  libs\\libc.prx, libs\\libkernel.prx      fresh build (Build\\anyps5): GuestArenaDonate and the
                                          APS5_GPU_ENGINE=kyty direct-memory forwarding
  libs\\libSceAgc.prx, libSceAgcDriver.prx, libSceVideoOut.prx
                                          forwarder DLLs: each NID resolves to kyty_gpu.dll, the
                                          two AnyPS5-internal HDR calls to astrohybrid_host.dll
  kyty_gpu.dll (+ .pdb)                   Build\\kyty-gpu (Tools\\hybrid\\build-kyty.ps1), in the exe
                                          directory so the loader finds it when it resolves the forwarders
  NVIDIA / AMD runtime files              DLSS, DLSS frame generation, Reflex (Streamline SDK 2.14.16
                                          bin\\x64) and FSR (FSR SDK 2.4.0 signedbin), with their licences,
                                          when the SDKs are in Tools\\sdk
  astrohybrid_host.dll                    AnyPS5's libAstroHybridHost.prx under the name kyty_gpu
                                          is told to load (KYTY_GPU_HOST_MODULE)
  app0                                    junction/symlink to the selected game dump (read only)
  app0.zar                                sibling hard link/copy when the dump uses streamed data
  _sd_mem, _trophy                        copies of Build\\Run's, made once
  AstroBot-NonRT.exe                      AstroBot-NonTiled.exe plus the second change of the Kyty
                                          launcher's Candidate-NonRT game patch (GI probes and
                                          lighting shaders off), checked byte for byte before writing
  u59-preset.json, u59-preset.cmd         the Kyty engine's environment preset (Jetsku int21,
                                          tools\\u59-preset.json in the Kyty checkout)
  _PipelineCache                          copy of a warm pipeline cache (ASTROHYBRID_KYTY_PIPELINE_CACHE), once

Usage: python -I Tools/hybrid/make_run_hybrid.py [--game DUMP]
       python -I Tools/hybrid/make_run_hybrid.py --layout-only --game DUMP --output RUN
The dump defaults to ASTROHYBRID_GAME_DUMP, then Build\\Run\\app0, then Game\\PPSA21567-app.
Every input path is under this repository by default; the environment overrides them:
  ASTROHYBRID_KYTY_SRC      patched Kyty checkout (Tools\\kyty-int21)
  ASTROHYBRID_KYTY_GPU      pgo (Build\\kyty-gpu, default), plain (Build\\kyty-plain-gpu) or a folder
  ASTROHYBRID_LLVM          folder with clang-cl.exe and lld-link.exe (Tools\\llvm-22.1.3\\bin, then VS's LLVM)
  ASTROHYBRID_STREAMLINE_DIR, ASTROHYBRID_FFX_DIR   the SDKs (Tools\\sdk\\streamline-v2.14.16,
                            Tools\\sdk\\fidelityfx-v2.4.0\\Kits\\FidelityFX)
"""

import argparse
import os
import pathlib
import re
import shutil
import stat
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
RUN = ROOT / "Build" / "Run"
HYBRID = ROOT / "Build" / "RunHybrid"
BUILT = ROOT / "Build" / "anyps5" / "core" / "libs" / "libs"
def _first_existing(*paths):
    return next((pathlib.Path(p) for p in paths if p and pathlib.Path(p).exists()), None)


KYTY_SRC = pathlib.Path(os.environ.get("ASTROHYBRID_KYTY_SRC") or ROOT / "Tools" / "kyty-int21")
# The PGO + ThinLTO build (Jetsku's int21 recipe) is about 4 fps faster in Sky Garden than the plain one;
# ASTROHYBRID_KYTY_GPU=plain or a directory picks another build.
KYTY_GPU_BUILDS = {"pgo": ROOT / "Build" / "kyty-gpu", "plain": ROOT / "Build" / "kyty-plain-gpu"}
_choice = os.environ.get("ASTROHYBRID_KYTY_GPU", "pgo")
KYTY_GPU = KYTY_GPU_BUILDS.get(_choice, pathlib.Path(_choice))
if not (KYTY_GPU / "kyty_gpu.dll").exists() and _choice == "pgo":
    KYTY_GPU = KYTY_GPU_BUILDS["plain"]
NIDS = KYTY_SRC / "src" / "hybrid" / "kytyGpuNids.inc"
LLVM = _first_existing(
    os.environ.get("ASTROHYBRID_LLVM"), ROOT / "Tools" / "llvm-22.1.3" / "bin",
    r"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\Llvm\x64\bin",
    r"C:\Program Files\LLVM\bin") or pathlib.Path("clang-cl-not-found")
GAME = pathlib.Path(os.environ.get("ASTROHYBRID_GAME_DUMP") or
                    (RUN / "app0" if (RUN / "app0").is_dir() else ROOT / "Game" / "PPSA21567-app"))

SHIMS = ("libSceAgc.prx", "libSceAgcDriver.prx", "libSceVideoOut.prx")
FRESH = ("libc.prx", "libkernel.prx", "libScePad.prx")
KYTY_PRESET = pathlib.Path(os.environ.get("ASTROHYBRID_KYTY_PRESET") or KYTY_SRC / "tools" / "u59-preset.json")
KYTY_CACHE = pathlib.Path(os.environ["ASTROHYBRID_KYTY_PIPELINE_CACHE"]) if os.environ.get("ASTROHYBRID_KYTY_PIPELINE_CACHE") else None
STREAMLINE = _first_existing(os.environ.get("ASTROHYBRID_STREAMLINE_DIR"), ROOT / "Tools" / "sdk" / "streamline-v2.14.16")
FFX = _first_existing(os.environ.get("ASTROHYBRID_FFX_DIR"),
                      ROOT / "Tools" / "sdk" / "fidelityfx-v2.4.0" / "Kits" / "FidelityFX")
# Runtime files the launcher and Tools\release\make_release.ps1 expect next to the game, from the SDKs
# (target name -> path inside the SDK). The licences go along under the names make_release.ps1 ships.
STREAMLINE_FILES = {name: pathlib.Path("bin/x64") / name for name in (
    "nvngx_dlss.dll", "nvngx_dlssg.dll", "NvLowLatencyVk.dll", "sl.interposer.dll", "sl.common.dll",
    "sl.dlss_g.dll", "sl.reflex.dll", "sl.pcl.dll", "nvngx_dlss.license.txt", "reflex.license.txt")}
STREAMLINE_FILES["streamline.license.txt"] = pathlib.Path("license.txt")
FFX_FILES = {name: pathlib.Path("signedbin") / name for name in (
    "amd_fidelityfx_loader_vk.dll", "amd_fidelityfx_upscaler_vk.dll", "amd_fidelityfx_framegeneration_vk.dll")}
FFX_FILES["amd_fsr.license.txt"] = pathlib.Path("docs/license.md")
# Candidate-NonRT.json (Kyty launcher) patches the original eboot at 0x73B451F and 0x70CE313. The
# relinked code sits 0x10000 further into AstroBot.exe; AstroBot-NonTiled.exe already has the first.
NON_RT = (
    (0x73C451F, "4584F60F84F10800004531F64C8D3D3EBBA90141B430", "4584F6E9F2080000904531F64C8D3D3EBBA90141B430"),
    (0x70DE313, "0FB682A00700008887E4050000", "B80000000090908887E4050000"),
)

# AstroBot-NonRT-SoftAssert.exe: the draw command writer's out-of-space callback (eboot 0x8A0C10 - 0x3F0000,
# "There are too many objects/batches in this scene", command_buffer_writer.ppr.cpp:31) skips the assert dialog and the
# int 0x41/0x42 traps: it prints the engine's own log line ("graphics command buffer ...") and returns false, so the writer takes the engine's own failure path (the inline block is
# marked failed and the frame goes on). A stop-gap for the intermittent Sky Garden arrival overflow.
SOFT_ASSERT = (
    (0x4B0C10,
     "554889e5488d3ded6fbb07488d0dc47fc2074c8d052299e407be1f000000ba0100000031c0e8660eb6ff85c07402cd42488d3d3c54d20731c0e88235fbffcd4131c05dc3",
     "554889e5eb2a3ded6fbb07488d0dc47fc2074c8d052299e407be1f000000ba0100000031c0e8660eb6ff85c07402cd42488d3d3c54d20731c0e88235fbff909031c05dc3"),
)

HOST_FORWARDS = {
    "libSceAgcDriver.prx": {
        "doCCHPPfxkI": "astrohybrid_host.AstroHybridHostHdr_nid_no_patch",
        "KMphJjTcNgk": "astrohybrid_host.AstroHybridNoteReportedToneMapPeak_nid_no_patch",
    },
}


def same_file(source, target):
    try:
        return os.path.samefile(source, target)
    except OSError:
        return False


def remove_generated_path(path):
    """Remove a generated entry, never following symlinks or Windows junctions."""
    path = pathlib.Path(path)
    try:
        info = path.lstat()
    except FileNotFoundError:
        return
    if stat.S_ISLNK(info.st_mode):
        path.unlink()
    elif getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT:
        # lstat also sees broken junctions; removing one must not recurse into the dump.
        if info.st_file_attributes & stat.FILE_ATTRIBUTE_DIRECTORY:
            os.rmdir(path)
        else:
            path.unlink()
    elif stat.S_ISDIR(info.st_mode):
        for child in path.iterdir():
            remove_generated_path(child)
        path.rmdir()
    else:
        path.unlink()


def link(source, target):
    source, target = pathlib.Path(source).resolve(), pathlib.Path(target)
    if not source.is_file():
        raise FileNotFoundError(f"missing file: {source}")
    if same_file(source, target):
        return
    remove_generated_path(target)
    try:
        os.link(source, target)
    except OSError:
        # Cross-volume/network dumps cannot be hard linked. Copy only this file, not the dump.
        shutil.copy2(source, target)


def copy(source, target):
    source, target = pathlib.Path(source).resolve(), pathlib.Path(target)
    if not source.is_file():
        raise FileNotFoundError(f"missing file: {source}")
    if same_file(source, target):
        return
    # Detach old hard links/symlinks before overwriting so Build\\Run and the dump stay unchanged.
    remove_generated_path(target)
    shutil.copy2(source, target)


def link_directory(source, target):
    if same_file(source, target):
        return
    remove_generated_path(target)
    if os.name != "nt":
        target.symlink_to(source, target_is_directory=True)
        return
    # Junctions do not need elevation; directory symlinks also support network drives.
    for flag in ("/J", "/D"):
        result = subprocess.run(["cmd", "/c", "mklink", flag, str(target), str(source)],
                                capture_output=True, text=True)
        if result.returncode == 0:
            return
        remove_generated_path(target)
    raise OSError(f"Cannot link {source}: {result.stdout.strip()} {result.stderr.strip()}. "
                  "For a network dump, enable Windows Developer Mode or run once as administrator.")


def link_split_app0(game, app0):
    """Build app0 as a real folder: one link per dump folder, links to its top-level files and a real copy
    of sce_module. The relinker writes the converted modules (*.guest.prx) into <output>\\app0\\sce_module,
    so in a runtime that gets relinked app0\\sce_module must not be the dump's own folder."""
    remove_generated_path(app0)
    app0.mkdir()
    for child in game.iterdir():
        target = app0 / child.name
        if child.name.lower() == "app0.zar":
            continue
        if child.is_dir() and child.name.lower() == "sce_module":
            target.mkdir()
            for module in child.iterdir():
                if module.is_file() and not module.name.lower().endswith(".guest.prx"):
                    shutil.copy2(module, target / module.name)
        elif child.is_dir():
            link_directory(child, target)
        elif child.is_file():
            link(child, target)


def setup_game_layout(game, output, split_modules=False):
    """Share the dump as app0 and place its streamed data archive beside it, without extraction.
    split_modules builds app0 for a relink (link_split_app0) instead of one link to the whole dump."""
    game, output = pathlib.Path(game).resolve(), pathlib.Path(output).resolve()
    app0 = output / "app0"
    if output == game or game in output.parents or app0 == game or app0 in game.parents:
        raise ValueError("The runtime layout must not overwrite or be created inside the source dump")
    if not game.is_dir():
        raise FileNotFoundError(f"missing game dump: {game}")
    archive = game / "app0.zar"
    has_archive = archive.is_file() and archive.stat().st_size > 0
    if os.path.lexists(archive) and not has_archive:
        raise ValueError(f"not a usable archive: {archive}")
    data = game / "data"
    if not has_archive and not data.is_dir():
        raise ValueError(f"{game}: neither data nor app0.zar is available")
    output.mkdir(parents=True, exist_ok=True)
    if split_modules:
        link_split_app0(game, app0)
    else:
        link_directory(game, app0)
    if has_archive:
        link(archive, output / "app0.zar")
    else:
        # A plain dump must not accidentally mount a previous dump's streamed data.
        remove_generated_path(output / "app0.zar")


def build_shim(library, nids, out):
    work = HYBRID / "_shims"
    work.mkdir(exist_ok=True)
    empty = work / "empty.c"
    empty.write_text("int astrohybrid_shim;\n", encoding="ascii")
    obj = work / "empty.obj"
    subprocess.run([str(LLVM / "clang-cl.exe"), "/nologo", "/c", f"/Fo{obj}", str(empty)], check=True)
    lines = [f"LIBRARY {library}", "EXPORTS"]
    for nid in nids:
        lines.append(f'    "{nid}" = kyty_gpu.{nid}')
    for nid, target in HOST_FORWARDS.get(library, {}).items():
        lines.append(f'    "{nid}" = {target}')
    definition = work / (library + ".def")
    definition.write_text("\n".join(lines) + "\n", encoding="ascii")
    subprocess.run([str(LLVM / "lld-link.exe"), "/NOLOGO", "/DLL", "/NOENTRY", "/MACHINE:X64", f"/DEF:{definition}",
                    f"/OUT:{out}", f"/IMPLIB:{work / (library + '.lib')}", str(obj)], check=True)
    return len(lines) - 2


def make_non_rt(source, target, patches=NON_RT):
    data = bytearray(source.read_bytes())
    for offset, before, after in patches:
        before, after = bytes.fromhex(before), bytes.fromhex(after)
        current = bytes(data[offset:offset + len(before)])
        if current == after:
            continue
        if current != before or data.count(before) != 1:
            sys.exit(f"{source.name}: unexpected bytes at 0x{offset:X}, not patching")
        data[offset:offset + len(before)] = after
    if not target.exists() or target.read_bytes() != data:
        target.write_bytes(bytes(data))


def main(game=GAME):
    if not (RUN / "AstroBot.exe").exists():
        sys.exit("Build\\Run\\AstroBot.exe is missing")
    for required in [KYTY_GPU / "kyty_gpu.dll", BUILT / "libAstroHybridHost.prx", NIDS] + [BUILT / name for name in FRESH]:
        if not required.exists():
            sys.exit(f"missing {required}")
    setup_game_layout(game, HYBRID, split_modules=True)
    # The converted modules the relink wrote into Build\Run\app0\sce_module (Tools\build.ps1); the game
    # loads them from app0\sce_module next to the originals.
    guest_modules = sorted((RUN / "app0" / "sce_module").glob("*.guest.prx"))
    if not guest_modules:
        sys.exit("Build\\Run\\app0\\sce_module has no *.guest.prx: relink with Tools\\build.ps1 first")
    for module in guest_modules:
        copy(module, HYBRID / "app0" / "sce_module" / module.name)
    libs = HYBRID / "libs"
    libs.mkdir(parents=True, exist_ok=True)
    for item in RUN.iterdir():
        if item.is_file() and item.name.startswith("AstroBot") and not item.name.startswith(("AstroBot-1080p", "AstroBot-NonTiled")):
            link(item, HYBRID / item.name)
    replaced = {name.lower() for name in SHIMS + FRESH}
    linked = 0
    for item in (RUN / "libs").iterdir():
        if item.is_file() and item.name.lower() not in replaced:
            link(item, libs / item.name)
            linked += 1
    for name in FRESH:
        copy(BUILT / name, libs / name)
    copy(BUILT / "libAstroHybridHost.prx", HYBRID / "astrohybrid_host.dll")
    for name in ("kyty_gpu.dll", "kyty_gpu.pdb"):
        if (KYTY_GPU / name).exists():
            copy(KYTY_GPU / name, HYBRID / name)
    rows = [re.match(r'KYTY_GPU_NID\(\d+, "([^"]+)", "([^"]+)"', line) for line in NIDS.read_text(encoding="utf-8").splitlines()]
    per_library = {name: [m.group(1) for m in rows if m and m.group(2) == name] for name in SHIMS}
    shims = {name: build_shim(name, nids, libs / name) for name, nids in per_library.items()}
    if (RUN / "AstroBot-NonTiled.exe").exists():
        make_non_rt(RUN / "AstroBot-NonTiled.exe", HYBRID / "AstroBot-NonRT.exe")
        make_non_rt(RUN / "AstroBot-NonTiled.exe", HYBRID / "AstroBot-NonRT-SoftAssert.exe", NON_RT + SOFT_ASSERT)
        for item in RUN.iterdir():
            if item.is_file() and item.name.startswith("AstroBot-NonTiled.") and item.suffix == ".json":
                link(item, HYBRID / item.name.replace("AstroBot-NonTiled.", "AstroBot-NonRT."))
                link(item, HYBRID / item.name.replace("AstroBot-NonTiled.", "AstroBot-NonRT-SoftAssert."))
    if KYTY_PRESET.exists():
        copy(KYTY_PRESET, HYBRID / "u59-preset.json")
        preset = __import__("json").loads(KYTY_PRESET.read_text(encoding="utf-8"))
        lines = ["@rem Kyty engine preset (generated from " + str(KYTY_PRESET) + ")"]
        lines += [f'if not defined {key} set "{key}={value}"' for key, value in preset.items()]
        (HYBRID / "u59-preset.cmd").write_text("\r\n".join(lines) + "\r\n", encoding="ascii")
    vendor = []
    for sdk, files in ((STREAMLINE, STREAMLINE_FILES), (FFX, FFX_FILES)):
        if sdk is None:
            continue
        for target, source in files.items():
            if (sdk / source).exists():
                copy(sdk / source, HYBRID / target)
                vendor.append(target)
    if KYTY_CACHE and KYTY_CACHE.exists() and not (HYBRID / "_PipelineCache").exists():
        shutil.copytree(KYTY_CACHE, HYBRID / "_PipelineCache")
    for name in ("_sd_mem", "_trophy"):
        if (RUN / name).exists() and not (HYBRID / name).exists():
            shutil.copytree(RUN / name, HYBRID / name)
    print(f"RunHybrid: {linked} prx linked, fresh {', '.join(FRESH)}, shims " + ", ".join(f"{k} {v} exports" for k, v in shims.items()))
    print(f"kyty_gpu.dll from {KYTY_GPU}")
    print(f"vendor runtime: {len(vendor)} files" + ("" if vendor else " (no SDK in Tools\\sdk: DLSS/FSR unavailable)"))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game", type=pathlib.Path, default=GAME, help="source game dump (never modified)")
    parser.add_argument("--layout-only", action="store_true", help="only link app0 and its sibling archive")
    parser.add_argument("--output", type=pathlib.Path, default=HYBRID, help="runtime directory for --layout-only")
    parser.add_argument("--split-modules", action="store_true",
                        help="with --layout-only: app0 for a relink (real sce_module copy, the dump is never written)")
    args = parser.parse_args()
    if args.layout_only:
        setup_game_layout(args.game, args.output, args.split_modules)
    else:
        if args.output != HYBRID:
            parser.error("--output requires --layout-only")
        main(args.game)
