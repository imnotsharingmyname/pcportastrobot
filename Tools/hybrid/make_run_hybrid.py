"""Assemble Build\\RunHybrid: AstroBot.exe with Kyty's GPU stack (AstroHybrid, milestone 2).

Build\\Run stays the working AnyPS5 build and is only read. RunHybrid gets:
  AstroBot.exe and its registries        hard links to Build\\Run (same files, no copies)
  libs\\*.prx (all others)                hard links to Build\\Run\\libs
  libs\\libc.prx, libs\\libkernel.prx      fresh build (Build\\anyps5): GuestArenaDonate and the
                                          APS5_GPU_ENGINE=kyty direct-memory forwarding
  libs\\libSceAgc.prx, libSceAgcDriver.prx, libSceVideoOut.prx
                                          forwarder DLLs: each NID resolves to kyty_gpu.dll, the
                                          two AnyPS5-internal HDR calls to astrohybrid_host.dll
  kyty_gpu.dll (+ .pdb)                   E:\\AstroHybrid-Work\\build\\kyty-gpu (exe directory, so
                                          the loader finds it when it resolves the forwarders)
  astrohybrid_host.dll                    AnyPS5's libAstroHybridHost.prx under the name kyty_gpu
                                          is told to load (KYTY_GPU_HOST_MODULE)
  app0                                    junction to Game\\PPSA21567-app (read only)
  _sd_mem, _trophy                        copies of Build\\Run's, made once
  AstroBot-NonRT.exe                      AstroBot-NonTiled.exe plus the second change of the Kyty
                                          launcher's Candidate-NonRT game patch (GI probes and
                                          lighting shaders off), checked byte for byte before writing
  u59-preset.json, u59-preset.cmd         the Kyty engine's environment preset (Jetsku int21)
  _PipelineCache                          copy of Kyty's warm pipeline cache, made once

Usage: python -I Tools/hybrid/make_run_hybrid.py
"""

import os
import pathlib
import re
import shutil
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
RUN = ROOT / "Build" / "Run"
HYBRID = ROOT / "Build" / "RunHybrid"
BUILT = ROOT / "Build" / "anyps5" / "core" / "libs" / "libs"
KYTY = pathlib.Path(r"E:\AstroHybrid-Work")
# The PGO + ThinLTO build (Jetsku's int21 recipe) is about 4 fps faster in Sky Garden than the plain one;
# ASTROHYBRID_KYTY_GPU=plain or a directory picks another build.
KYTY_GPU_BUILDS = {"pgo": KYTY / "build" / "kyty-gpu-pgo", "plain": KYTY / "build" / "kyty-gpu"}
_choice = os.environ.get("ASTROHYBRID_KYTY_GPU", "pgo")
KYTY_GPU = KYTY_GPU_BUILDS.get(_choice, pathlib.Path(_choice))
if not (KYTY_GPU / "kyty_gpu.dll").exists() and _choice == "pgo":
    KYTY_GPU = KYTY_GPU_BUILDS["plain"]
NIDS = KYTY / "source" / "kyty" / "src" / "hybrid" / "kytyGpuNids.inc"
LLVM = pathlib.Path(r"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\Llvm\x64\bin")
GAME = ROOT / "Game" / "PPSA21567-app"

SHIMS = ("libSceAgc.prx", "libSceAgcDriver.prx", "libSceVideoOut.prx")
FRESH = ("libc.prx", "libkernel.prx", "libScePad.prx")
KYTY_PRESET = pathlib.Path(r"E:\AstroBotPC\.engines\Jetsku-int21\u59-preset.json")
KYTY_CACHE = KYTY / "runtime" / "astrohybrid" / "_PipelineCache"
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


def link(source, target):
    if target.exists():
        if os.path.samefile(source, target):
            return
        target.unlink()
    os.link(source, target)


def copy(source, target):
    shutil.copy2(source, target)


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


def main():
    if not (RUN / "AstroBot.exe").exists():
        sys.exit("Build\\Run\\AstroBot.exe is missing")
    for required in [KYTY_GPU / "kyty_gpu.dll", BUILT / "libAstroHybridHost.prx", NIDS] + [BUILT / name for name in FRESH]:
        if not required.exists():
            sys.exit(f"missing {required}")
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
    app0 = HYBRID / "app0"
    if not app0.exists():
        subprocess.run(["cmd", "/c", "mklink", "/J", str(app0), str(GAME)], check=True, capture_output=True)
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
    if KYTY_CACHE.exists() and not (HYBRID / "_PipelineCache").exists():
        shutil.copytree(KYTY_CACHE, HYBRID / "_PipelineCache")
    for name in ("_sd_mem", "_trophy"):
        if (RUN / name).exists() and not (HYBRID / name).exists():
            shutil.copytree(RUN / name, HYBRID / name)
    print(f"RunHybrid: {linked} prx linked, fresh {', '.join(FRESH)}, shims " + ", ".join(f"{k} {v} exports" for k, v in shims.items()))
    print(f"kyty_gpu.dll from {KYTY_GPU}")


if __name__ == "__main__":
    main()
