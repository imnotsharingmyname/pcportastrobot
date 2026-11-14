#!/usr/bin/env python3
"""Build kyty_gpu.so from an existing Linux kyty_emulator build.

The Windows tree uses MSVC/clang-cl objects and a .def file.  On Linux we reuse
the emulator's own objects (built with PIC) and add the hybrid bridge plus one
ELF alias per NID, so the .so exports the names AnyPS5's relinked game imports.
"""

from __future__ import annotations

import argparse
import pathlib
import re
import shlex
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
KYTY_SRC = ROOT / "Tools" / "KytyPS5-jetsku"


def cache_value(build: pathlib.Path, name: str) -> str | None:
    for line in (build / "CMakeCache.txt").read_text(encoding="utf-8", errors="replace").splitlines():
        if line.startswith(name + ":"):
            return line.split("=", 1)[1]
    return None


def ninja_block(text: str, header: str) -> str:
    start = text.find(header)
    if start < 0:
        sys.exit(f"build.ninja has no {header!r}")
    end = text.find("\n\n", start)
    return text[start:end]


def variables(block: str) -> dict[str, str]:
    values: dict[str, str] = {}
    for line in block.splitlines()[1:]:
        match = re.match(r"\s+(\w+) = (.*)", line)
        if match:
            values[match.group(1)] = match.group(2)
    return values


def ninja_paths(text: str) -> list[str]:
    return [part.replace("$:", ":").replace("$ ", " ") for part in re.split(r"(?<!\$) ", text) if part]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build", default=str(ROOT / "Build" / "kyty"))
    parser.add_argument("--out", default=str(ROOT / "Build" / "kyty-gpu"))
    args = parser.parse_args()

    build = pathlib.Path(args.build).resolve()
    out = pathlib.Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)

    ninja = (build / "build.ninja").read_text(encoding="utf-8", errors="replace")
    compile_block = variables(ninja_block(ninja, "build CMakeFiles/kyty_emulator.dir/src/libs/agc.cpp.o:"))
    link_header = ninja_block(ninja, "build kyty_emulator:")
    link_vars = variables(link_header)
    inputs = link_header.splitlines()[0].split(":", 1)[1].split("|")[0]
    objects = [p for p in ninja_paths(inputs)[1:] if p.endswith(".o") and not p.endswith("main.cpp.o")]

    compiler = cache_value(build, "CMAKE_CXX_COMPILER") or "g++"
    source_root = pathlib.Path(cache_value(build, "CMAKE_HOME_DIRECTORY") or KYTY_SRC)

    defines = shlex.split(compile_block.get("DEFINES", ""))
    flags = shlex.split(compile_block.get("FLAGS", ""))
    includes = shlex.split(compile_block.get("INCLUDES", ""))
    # The emulator target is PIE; the same objects are usable in a shared object on x86-64.
    # Put -fPIC last so GCC/Clang take it over CMake's -fPIE.
    if "-fPIC" not in flags:
        flags.append("-fPIC")

    nids_path = source_root / "src" / "hybrid" / "kytyGpuNids.inc"
    nid_rows = [
        (int(index), nid, library)
        for index, nid, library in re.findall(
            r'KYTY_GPU_NID\((\d+),\s*"([^"]+)",\s*"([^"]+)"',
            nids_path.read_text(encoding="utf-8"))
    ]
    if not nid_rows:
        sys.exit(f"no NIDs in {nids_path}")
    # Kernel memory NIDs are reached through KytyGpuGetNid, not through the ELF global scope:
    # exporting them from the graphics shim would shadow libkernel's own NIDs.
    shim_rows = [row for row in nid_rows if row[2] != "libkernel.prx"]

    # libSceAgcDriver also exports two AnyPS5 host calls; the bridge forwards them to
    # astrohybrid_host.  The games import them from libSceAgcDriver.prx.
    host_nids = {
        "doCCHPPfxkI": "KytyGpuAstroHdr",
        "KMphJjTcNgk": "KytyGpuAstroToneMapPeak",
    }

    bridge = out / "kytyGpuBridge.o"
    bridge_cmd = [compiler, "-c", *defines, *flags, *includes,
                  str(source_root / "src" / "hybrid" / "kytyGpuBridge.cpp"), "-o", str(bridge)]
    print(f"compile: {source_root / 'src/hybrid/kytyGpuBridge.cpp'}", flush=True)
    subprocess.run(bridge_cmd, cwd=build, check=True)

    aliases = out / "kytyGpuNids.s"
    alias_lines = [".text"]
    # A .set alias to a symbol defined in another object is dropped by the assembler, so emit a
    # real two-instruction forwarder for each imported name.
    for index, nid, _library in shim_rows:
        alias_lines += [
            f'.globl "{nid}"',
            f'.type "{nid}", @function',
            f'"{nid}":',
            f"    jmp kyty_gpu_nid_{index}",
        ]
    for nid, function in host_nids.items():
        alias_lines += [
            f'.globl "{nid}"',
            f'.type "{nid}", @function',
            f'"{nid}":',
            f"    jmp {function}",
        ]
    aliases.write_text("\n".join(alias_lines) + "\n", encoding="utf-8")
    alias_obj = out / "kytyGpuNids.o"
    subprocess.run([compiler, "-c", "-fPIC", str(aliases), "-o", str(alias_obj)], check=True)

    version_map = out / "kyty_gpu.map"
    map_lines = ["{", "  global:", "    KytyGpuGetApi;", "    KytyGpuGetNid;"]
    map_lines += [f'    "{nid}";' for _index, nid, _library in shim_rows]
    map_lines += [f'    "{nid}";' for nid in host_nids]
    map_lines += ["  local:", "    *;", "};", ""]
    version_map.write_text("\n".join(map_lines), encoding="utf-8")

    libraries = ninja_paths(link_vars.get("LINK_LIBRARIES", ""))
    link_flags = shlex.split(link_vars.get("LINK_FLAGS", ""))
    resolved_libraries: list[str] = []
    for library in libraries:
        path = pathlib.Path(library)
        if library.startswith("-"):
            resolved_libraries.append(library)
        elif path.is_absolute():
            resolved_libraries.append(str(path))
        elif len(path.parts) == 1:
            resolved_libraries.append(library)
        else:
            resolved_libraries.append(str(build / path))

    output = out / "kyty_gpu.so"
    command = [compiler, "-shared", "-o", str(output), *link_flags,
               "-Wl,--version-script=" + str(version_map),
               "-Wl,-soname,kyty_gpu.so",
               str(bridge), str(alias_obj)]
    command += [str(build / obj) for obj in objects]
    command += resolved_libraries

    print(f"link: {len(objects)} emulator objects + bridge + {len(shim_rows)} graphics NIDs", flush=True)
    subprocess.run(command, cwd=build, check=True)
    print(f"built {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
