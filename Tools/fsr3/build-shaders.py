#!/usr/bin/env python3
"""Compile FSR 3.1 HLSL to SPIR-V and generate the SDK's shader-blob tables.

The FidelityFX SDK ships the FSR 3.1 shader sources but not the generated
``*_permutations.h`` files that ``ffx_fsr3upscaler_shaderblobs.cpp`` includes;
AMD generates those on Windows as part of the sample build. This script does the
same job on Linux:

  * compiles every pass of the FSR 3.1 upscaler with DXC to SPIR-V, once per
    permutation key (64 keys: six boolean shader options);
  * reflects each module's descriptors out of the SPIR-V, because ``FfxShaderBlob``
    carries the binding counts, indices and names rather than deriving them;
  * writes one ``.c`` + ``.h`` pair per pass with a 64-entry PermutationInfo and
    an identity indirection table, so every key resolves to the module compiled
    for that key.

Only the generic (non-fp16) path is built. The context is created without
``FFX_FSR3UPSCALER_ENABLE_16BIT``, so the fp16 tables are never requested.

Usage:
  Tools/fsr3/build-shaders.py --sdk <FidelityFX SDK> --dxc </path/to/dxc> \
      --out Build/fsr3/shaders
"""

from __future__ import annotations

import argparse
import json
import pathlib
import shutil
import struct
import subprocess
import sys

SPIRV_MAGIC = 0x07230203
OP_NAME = 5
OP_TYPE_IMAGE = 25
OP_TYPE_SAMPLER = 26
OP_TYPE_SAMPLED_IMAGE = 27
OP_TYPE_ARRAY = 28
OP_TYPE_RUNTIME_ARRAY = 29
OP_TYPE_STRUCT = 30
OP_TYPE_POINTER = 32
OP_VARIABLE = 59
OP_DECORATE = 71

DEC_BINDING = 33
DEC_DESCRIPTOR_SET = 34

STORAGE_UNIFORM_CONSTANT = 0
STORAGE_UNIFORM = 2
STORAGE_STORAGE_BUFFER = 12

# Bit order must match POPULATE_PERMUTATION_KEY in ffx_fsr3upscaler_shaderblobs.cpp.
KEY_ORDER = [
    "FFX_FSR3UPSCALER_OPTION_REPROJECT_USE_LANCZOS_TYPE",
    "FFX_FSR3UPSCALER_OPTION_HDR_COLOR_INPUT",
    "FFX_FSR3UPSCALER_OPTION_LOW_RESOLUTION_MOTION_VECTORS",
    "FFX_FSR3UPSCALER_OPTION_JITTERED_MOTION_VECTORS",
    "FFX_FSR3UPSCALER_OPTION_INVERTED_DEPTH",
    "FFX_FSR3UPSCALER_OPTION_APPLY_SHARPENING",
]

UPSCALER_PASSES = {
    "prepare_inputs_pass": "ffx_fsr3upscaler_prepare_inputs_pass.hlsl",
    "luma_pyramid_pass": "ffx_fsr3upscaler_luma_pyramid_pass.hlsl",
    "shading_change_pyramid_pass": "ffx_fsr3upscaler_shading_change_pyramid_pass.hlsl",
    "shading_change_pass": "ffx_fsr3upscaler_shading_change_pass.hlsl",
    "prepare_reactivity_pass": "ffx_fsr3upscaler_prepare_reactivity_pass.hlsl",
    "luma_instability_pass": "ffx_fsr3upscaler_luma_instability_pass.hlsl",
    "accumulate_pass": "ffx_fsr3upscaler_accumulate_pass.hlsl",
    "rcas_pass": "ffx_fsr3upscaler_rcas_pass.hlsl",
    "debug_view_pass": "ffx_fsr3upscaler_debug_view_pass.hlsl",
    "autogen_reactive_pass": "ffx_fsr3upscaler_autogen_reactive_pass.hlsl",
}

ORDERING = [
    ("cb", "boundConstantBuffer"),
    ("srv_texture", "boundSRVTexture"),
    ("uav_texture", "boundUAVTexture"),
    ("srv_buffer", "boundSRVBuffer"),
    ("uav_buffer", "boundUAVBuffer"),
    ("sampler", "boundSampler"),
]


def _string(words: list[int], start: int) -> str:
    chars: list[str] = []
    index = start
    while index < len(words):
        value = words[index]
        for shift in range(0, 32, 8):
            code = (value >> shift) & 0xFF
            if code == 0:
                return "".join(chars)
            chars.append(chr(code))
        index += 1
    return "".join(chars)


def reflect(blob: bytes) -> dict[str, list[tuple[int, str]]]:
    """Descriptor bindings of a SPIR-V module, classified for FfxShaderBlob."""
    words = list(struct.unpack_from(f"<{len(blob) // 4}I", blob, 0))
    if not words or words[0] != SPIRV_MAGIC:
        raise SystemExit("not a SPIR-V module")

    names: dict[int, str] = {}
    bindings: dict[int, int] = {}
    sets: dict[int, int] = {}
    pointers: dict[int, tuple[int, int]] = {}
    variables: dict[int, tuple[int, int]] = {}
    types: dict[int, tuple[int, ...]] = {}

    index = 5
    while index < len(words):
        head = words[index]
        opcode = head & 0xFFFF
        length = head >> 16
        if length == 0 or index + length > len(words):
            break
        operands = words[index + 1: index + length]

        if opcode == OP_NAME and len(operands) >= 2:
            names[operands[0]] = _string(words, index + 2)
        elif opcode == OP_DECORATE and len(operands) >= 3:
            if operands[1] == DEC_BINDING:
                bindings[operands[0]] = operands[2]
            elif operands[1] == DEC_DESCRIPTOR_SET:
                sets[operands[0]] = operands[2]
        elif opcode == OP_TYPE_POINTER and len(operands) >= 3:
            pointers[operands[0]] = (operands[1], operands[2])
        elif opcode == OP_VARIABLE and len(operands) >= 3:
            variables[operands[1]] = (operands[0], operands[2])
        elif opcode in (OP_TYPE_IMAGE, OP_TYPE_SAMPLER, OP_TYPE_SAMPLED_IMAGE,
                        OP_TYPE_STRUCT, OP_TYPE_ARRAY, OP_TYPE_RUNTIME_ARRAY):
            types[operands[0]] = (opcode,) + tuple(operands[1:])

        index += length

    groups: dict[str, list[tuple[int, str]]] = {key: [] for key, _ in ORDERING}

    def unwrap(type_id: int) -> tuple[int, ...]:
        seen = 0
        while type_id in types and types[type_id][0] in (OP_TYPE_ARRAY, OP_TYPE_RUNTIME_ARRAY) and seen < 8:
            type_id = types[type_id][1]
            seen += 1
        return types.get(type_id, (0,))

    for var_id, (pointer_type, storage) in variables.items():
        pointee = pointers.get(pointer_type, (storage, 0))[1]
        info = unwrap(pointee)
        kind = info[0] if info else 0
        binding = bindings.get(var_id)
        if binding is None:
            continue
        if sets.get(var_id, 0) != 0:
            raise SystemExit(f"{names.get(var_id, var_id)} is in descriptor set "
                             f"{sets[var_id]}; the FFX Vulkan backend uses one set")
        name = names.get(var_id, f"binding{binding}")

        if storage == STORAGE_UNIFORM:
            groups["cb"].append((binding, name))
        elif storage == STORAGE_UNIFORM_CONSTANT:
            if kind == OP_TYPE_SAMPLER:
                groups["sampler"].append((binding, name))
            elif kind in (OP_TYPE_IMAGE, OP_TYPE_SAMPLED_IMAGE):
                # OpTypeImage operands: sampled type, Dim, Depth, Arrayed, MS, Sampled.
                sampled = info[6] if len(info) > 6 else 1
                groups["uav_texture" if sampled == 2 else "srv_texture"].append((binding, name))
            else:
                groups["srv_buffer"].append((binding, name))
        elif storage == STORAGE_STORAGE_BUFFER:
            groups["uav_buffer"].append((binding, name))

    return {key: sorted(value) for key, value in groups.items()}


def _u32(name: str, values: list[int]) -> str:
    return f"static const uint32_t {name}[] = {{{','.join(str(v) for v in values)}}};"


def _str(name: str, values: list[str]) -> str:
    items = ",".join('"' + v.replace('"', "'") + '"' for v in values)
    return f"static const char* {name}[] = {{{items}}};"


def emit_pass(pass_key: str, modules: list[tuple[bytes, str, dict]],
              keys: list[int]) -> str:
    """Emit the .c body: one blob per permutation key plus the lookup table."""
    stem = f"ffx_fsr3upscaler_{pass_key}"
    lines: list[str] = ['#include <ffx_internal_types.h>', ""]

    for key, (blob, entry, reflection) in enumerate(modules):
        name = f"{stem}_{key}"
        lines.append(f"static const uint8_t {name}_data[] = {{"
                     + ",".join(str(b) for b in blob) + "};")
        for group, cname in ORDERING:
            pairs = reflection[group]
            if pairs:
                lines.append(_u32(f"{name}_{cname}s", [b for b, _ in pairs]))
                lines.append(_u32(f"{name}_{cname}Counts", [1] * len(pairs)))
                lines.append(_u32(f"{name}_{cname}Spaces", [0] * len(pairs)))
                lines.append(_str(f"{name}_{cname}Names", [n for _, n in pairs]))
            else:
                lines.append(_u32(f"{name}_{cname}s", [0]))
                lines.append(_u32(f"{name}_{cname}Counts", [0]))
                lines.append(_u32(f"{name}_{cname}Spaces", [0]))
                lines.append(_str(f"{name}_{cname}Names", [""]))
        lines.append(_u32(f"{name}_rtZero", [0]))

    entries: list[str] = []
    for key, (blob, entry, reflection) in enumerate(modules):
        name = f"{stem}_{key}"
        counts = {group: len(reflection[group]) for group, _ in ORDERING}
        entries.append(
            f"{{ {name}_data, sizeof({name}_data), \"{entry}\", "
            f"{counts['cb']}, {counts['srv_texture']}, {counts['uav_texture']}, "
            f"{counts['srv_buffer']}, {counts['uav_buffer']}, {counts['sampler']}, 0, "
            f"{name}_boundConstantBufferNames, {name}_boundConstantBuffers, {name}_boundConstantBufferCounts, {name}_boundConstantBufferSpaces, "
            f"{name}_boundSRVTextureNames, {name}_boundSRVTextures, {name}_boundSRVTextureCounts, {name}_boundSRVTextureSpaces, "
            f"{name}_boundUAVTextureNames, {name}_boundUAVTextures, {name}_boundUAVTextureCounts, {name}_boundUAVTextureSpaces, "
            f"{name}_boundSRVBufferNames, {name}_boundSRVBuffers, {name}_boundSRVBufferCounts, {name}_boundSRVBufferSpaces, "
            f"{name}_boundUAVBufferNames, {name}_boundUAVBuffers, {name}_boundUAVBufferCounts, {name}_boundUAVBufferSpaces, "
            f"{name}_boundSamplerNames, {name}_boundSamplers, {name}_boundSamplerCounts, {name}_boundSamplerSpaces, "
            f"{name}_rtZero, {name}_rtZero, {name}_rtZero, {name}_rtZero }}")

    lines.append(f"const FfxShaderBlob g_{stem}_PermutationInfo[] = {{")
    lines.append(",\n".join(entries))
    lines.append("};")
    # 64 entries, indexed by the permutation key. A key that was not built maps
    # to -1: the lookup then faults loudly instead of running a shader compiled
    # for different options.
    table = [-1] * 64
    for position, key in enumerate(keys):
        if 0 <= key < 64:
            table[key] = position
    lines.append(f"const int32_t g_{stem}_IndirectionTable[64] = "
                 + "{" + ",".join(str(v) for v in table) + "};")
    return "\n".join(lines) + "\n"


def compile_pass(dxc: str, source: pathlib.Path, include: list[pathlib.Path],
                 defines: dict[str, int], out: pathlib.Path) -> bytes:
    command = [dxc, "-T", "cs_6_0", "-E", "CS", "-spirv",
               "-fspv-target-env=vulkan1.3", "-fvk-use-dx-layout",
               "-DFFX_GPU=1", "-DFFX_HLSL=1"]
    command += [f"-D{name}={value}" for name, value in defines.items()]
    command += [f"-I{path}" for path in include]
    command += [str(source), "-Fo", str(out)]
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f"dxc failed for {source.name}:\n{result.stdout}\n{result.stderr}")
    return out.read_bytes()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--sdk", required=True, help="FidelityFX SDK root")
    parser.add_argument("--dxc", required=True, help="path to the dxc binary")
    parser.add_argument("--out", default="Build/fsr3/shaders")
    parser.add_argument("--keys", default="all", help="'all' or a comma list of key indices")
    args = parser.parse_args()

    sdk = pathlib.Path(args.sdk).expanduser().resolve()
    dxc = shutil.which(args.dxc) or args.dxc
    if not pathlib.Path(dxc).exists():
        raise SystemExit(f"dxc not found at {dxc}")

    kit = sdk / "Kits" / "FidelityFX" / "upscalers" / "fsr3"
    shaders = kit / "internal" / "shaders"
    include = [kit / "include" / "gpu", kit / "include", shaders]

    out = pathlib.Path(args.out).expanduser()
    out.mkdir(parents=True, exist_ok=True)
    work = out / "spv"
    work.mkdir(exist_ok=True)

    keys = list(range(64)) if args.keys == "all" else [int(v) for v in args.keys.split(",")]

    manifest: dict[str, dict] = {}
    for pass_key, filename in UPSCALER_PASSES.items():
        modules = []
        for key in keys:
            defines = {name: 1 if key & (1 << bit) else 0
                       for bit, name in enumerate(KEY_ORDER)}
            blob = compile_pass(dxc, shaders / filename, include, defines,
                                work / f"{pass_key}.{key}.spv")
            reflection = reflect(blob)
            modules.append((blob, "CS", reflection))
            manifest[f"{pass_key}@{key}"] = {
                "size": len(blob),
                "bindings": {k: [b for b, _ in v] for k, v in reflection.items()},
            }

        (out / f"ffx_fsr3upscaler_{pass_key}_permutations.c").write_text(
            emit_pass(pass_key, modules, keys))
        (out / f"ffx_fsr3upscaler_{pass_key}_permutations.h").write_text(
            "// Generated by Tools/fsr3/build-shaders.py\n#pragma once\n"
            f"extern const FfxShaderBlob g_ffx_fsr3upscaler_{pass_key}_PermutationInfo[];\n"
            f"extern const int32_t g_ffx_fsr3upscaler_{pass_key}_IndirectionTable[];\n")
        print(f"  {pass_key}: {len(modules)} modules")

    (out / "manifest.json").write_text(json.dumps(manifest, indent=1))
    print(f"== wrote {len(UPSCALER_PASSES)} passes to {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
