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

KITS = {
    "upscaler": {
        "root": "upscalers/fsr3",
        "prefix": "ffx_fsr3upscaler",
        "include": ["include/gpu", "include", "internal/shaders"],
        "keys": [
            "FFX_FSR3UPSCALER_OPTION_REPROJECT_USE_LANCZOS_TYPE",
            "FFX_FSR3UPSCALER_OPTION_HDR_COLOR_INPUT",
            "FFX_FSR3UPSCALER_OPTION_LOW_RESOLUTION_MOTION_VECTORS",
            "FFX_FSR3UPSCALER_OPTION_JITTERED_MOTION_VECTORS",
            "FFX_FSR3UPSCALER_OPTION_INVERTED_DEPTH",
            "FFX_FSR3UPSCALER_OPTION_APPLY_SHARPENING",
        ],
    },
    "frameinterpolation": {
        "root": "framegeneration/fsr3",
        "prefix": "ffx_frameinterpolation",
        "include": ["include/gpu", "include", "internal/shaders"],
        "keys": [
            "FFX_FRAMEINTERPOLATION_OPTION_LOW_RES_MOTION_VECTORS",
            "FFX_FRAMEINTERPOLATION_OPTION_JITTERED_MOTION_VECTORS",
            "FFX_FRAMEINTERPOLATION_OPTION_INVERTED_DEPTH",
        ],
        # The 2.4.0 drop calls MotionVectorJitterCancellation() in
        # ffx_frameinterpolation_callbacks_hlsl.h without defining it anywhere, so
        # those permutations fail to compile and used to be skipped. Skipping them
        # leaves -1 in the indirection table, which the provider indexes without a
        # bounds check -- the same crash the upscaler had. The call sits in the
        # jittered-motion-vector path and its obvious meaning is the current jitter,
        # so it is defined as Jitter() and every key is built.
        "call_macros": {
            "MotionVectorJitterCancellation()": "Jitter()",
        },
        # POPULATE_PERMUTATION_KEY writes this member name; the shader define is
        # spelled ..._JITTERED_....
        "key_names": {"FFX_FRAMEINTERPOLATION_OPTION_JITTERED_MOTION_VECTORS":
                      "FFX_FRAMEINTERPOLATION_OPTION_JITTER_MOTION_VECTORS"},
    },
    "opticalflow": {
        "root": "framegeneration/fsr3",
        "prefix": "ffx_opticalflow",
        "include": ["include/gpu", "include", "internal/shaders"],
        "keys": [
            "FFX_OPTICALFLOW_OPTION_HDR_COLOR_INPUT",
        ],
    },
}

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

FRAMEINTERPOLATION_PASSES = {
    "setup_pass": "ffx_frameinterpolation_setup_pass.hlsl",
    "reconstruct_previous_depth_pass": "ffx_frameinterpolation_reconstruct_previous_depth_pass.hlsl",
    "optical_flow_vector_field_pass": "ffx_frameinterpolation_optical_flow_vector_field_pass.hlsl",
    "game_motion_vector_field_pass": "ffx_frameinterpolation_game_motion_vector_field_pass.hlsl",
    "disocclusion_mask_pass": "ffx_frameinterpolation_disocclusion_mask_pass.hlsl",
    "reconstruct_and_dilate_pass": "ffx_frameinterpolation_reconstruct_and_dilate_pass.hlsl",
    "compute_inpainting_pyramid_pass": "ffx_frameinterpolation_compute_inpainting_pyramid_pass.hlsl",
    "compute_game_vector_field_inpainting_pyramid_pass":
        "ffx_frameinterpolation_compute_game_vector_field_inpainting_pyramid_pass.hlsl",
    "inpainting_pass": "ffx_frameinterpolation_inpainting_pass.hlsl",
    "pass": "ffx_frameinterpolation_pass.hlsl",
    "debug_view_pass": "ffx_frameinterpolation_debug_view_pass.hlsl",
}

OPTICALFLOW_PASSES = {
    "prepare_luma_pass": "ffx_opticalflow_prepare_luma_pass.hlsl",
    "compute_luminance_pyramid_pass": "ffx_opticalflow_compute_luminance_pyramid_pass.hlsl",
    "generate_scd_histogram_pass": "ffx_opticalflow_generate_scd_histogram_pass.hlsl",
    "compute_scd_divergence_pass": "ffx_opticalflow_compute_scd_divergence_pass.hlsl",
    "compute_optical_flow_advanced_pass_v5": "ffx_opticalflow_compute_optical_flow_advanced_pass_v5.hlsl",
    "filter_optical_flow_pass_v5": "ffx_opticalflow_filter_optical_flow_pass_v5.hlsl",
    "scale_optical_flow_advanced_pass_v5": "ffx_opticalflow_scale_optical_flow_advanced_pass_v5.hlsl",
}

PASSES_BY_KIT = {
    "upscaler": UPSCALER_PASSES,
    "frameinterpolation": FRAMEINTERPOLATION_PASSES,
    "opticalflow": OPTICALFLOW_PASSES,
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


# The field names POPULATE_SHADER_BLOB_FFX reads, grouped per binding class.
# (reflection key, singular stem used in the emitted array names)
FIELD_GROUPS = [
    ("cb", "constantBuffer"),
    ("srv_texture", "srvTexture"),
    ("uav_texture", "uavTexture"),
    ("srv_buffer", "srvBuffer"),
    ("uav_buffer", "uavBuffer"),
    ("sampler", "sampler"),
]

COUNTS = ["numConstantBuffers", "numSRVTextures", "numUAVTextures",
          "numSRVBuffers", "numUAVBuffers", "numSamplers",
          "numRTAccelerationStructures"]


def emit_pass(pass_key: str, modules: list[tuple[bytes, str, dict]],
              keys: list[int], prefix: str, options: list[str]) -> str:
    """Emit the .c body: one module per permutation key plus the lookup tables."""
    stem = f"{prefix}_{pass_key}"
    lines: list[str] = ['#include "fsr3_shader_blob_header.h"', ""]

    for key, (blob, entry, reflection) in enumerate(modules):
        name = f"{stem}_{key}"
        lines.append(f"static const uint8_t {name}_data[] = {{"
                     + ",".join(str(b) for b in blob) + "};")
        for group, singular in FIELD_GROUPS:
            pairs = reflection[group]
            if pairs:
                lines.append(_u32(f"{name}_{singular}Bindings", [b for b, _ in pairs]))
                lines.append(_u32(f"{name}_{singular}Counts", [1] * len(pairs)))
                lines.append(_u32(f"{name}_{singular}Spaces", [0] * len(pairs)))
                lines.append(_str(f"{name}_{singular}Names", [n for _, n in pairs]))
            else:
                lines.append(_u32(f"{name}_{singular}Bindings", [0]))
                lines.append(_u32(f"{name}_{singular}Counts", [0]))
                lines.append(_u32(f"{name}_{singular}Spaces", [0]))
                lines.append(_str(f"{name}_{singular}Names", [""]))
        lines.append(_u32(f"{name}_rtZero", [0]))
        lines.append(_str(f"{name}_rtNames", [""]))

    entries: list[str] = []
    for key, (blob, entry, reflection) in enumerate(modules):
        name = f"{stem}_{key}"
        counts = [len(reflection[group]) for group, _ in FIELD_GROUPS] + [0]
        fields = [f"{name}_data", f"sizeof({name}_data)", f'"{entry}"']
        fields += [str(value) for value in counts]
        for _group, singular in FIELD_GROUPS:
            fields += [f"{name}_{singular}Names", f"{name}_{singular}Bindings",
                       f"{name}_{singular}Counts", f"{name}_{singular}Spaces"]
        fields += [f"{name}_rtNames", f"{name}_rtZero", f"{name}_rtZero", f"{name}_rtZero"]
        entries.append("{ " + ", ".join(fields) + " }")

    lines.append(f"const FfxShaderBlobHeader g_{stem}_PermutationInfo[] = {{")
    lines.append(",\n".join(entries))
    lines.append("};")

    table = [-1] * 64
    for position, key in enumerate(keys):
        if 0 <= key < 64:
            table[key] = position
    table_text = "{" + ",".join(str(v) for v in table) + "}"
    lines.append(f"const int32_t g_{stem}_IndirectionTable[64] = " + table_text + ";")
    # The lookup asks for wave64 and 16-bit tables on AMD hardware. Subgroup size
    # is a device property in Vulkan and the modules are fp32, so the same
    # entries serve every name: a device asking for fp16 gets correct output with
    # less through-put, not a wrong image.
    for suffix in ("wave64", "16bit", "wave64_16bit"):
        lines.append(f"const FfxShaderBlobHeader g_{stem}_{suffix}_PermutationInfo[] = {{")
        lines.append(",\n".join(entries))
        lines.append("};")
        lines.append(f"const int32_t g_{stem}_{suffix}_IndirectionTable[64] = " + table_text + ";")
    return "\n".join(lines) + "\n"


def compile_pass(dxc: str, source: pathlib.Path, include: list[pathlib.Path],
                 defines: dict[str, int], out: pathlib.Path,
                 call_macros: dict[str, str] | None = None) -> bytes:
    call_macros = call_macros or {}
    # The FFX Vulkan backend puts every resource class in one descriptor set and
    # builds the layout from these numbers, so the classes must not collide:
    # SRV keeps t0, UAVs start at 128, constant buffers at 256 and samplers at
    # 320 (ffx_vk.cpp: SAMPLER_BINDING_SHIFT). Without the shifts a set has two
    # binding 0s and vkCreateDescriptorSetLayout fails, which surfaces as a
    # runtime error from ffxCreateContext.
    command = [dxc, "-T", "cs_6_0", "-E", "CS", "-spirv",
               "-fspv-target-env=vulkan1.3", "-fvk-use-dx-layout",
               "-fvk-u-shift", "128", "0",
               "-fvk-b-shift", "256", "0",
               "-fvk-s-shift", "320", "0",
               "-DFFX_GPU=1", "-DFFX_HLSL=1"]
    command += [f"-D{name}={value}" for name, value in defines.items()]
    command += [f"-D{name}={value}" for name, value in call_macros.items()]
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
    parser.add_argument("--kits", default=None,
                        help="comma list of upscaler,frameinterpolation,opticalflow (default: all)")
    args = parser.parse_args()

    sdk = pathlib.Path(args.sdk).expanduser().resolve()
    dxc = shutil.which(args.dxc) or args.dxc
    if not pathlib.Path(dxc).exists():
        raise SystemExit(f"dxc not found at {dxc}")

    out = pathlib.Path(args.out).expanduser()
    out.mkdir(parents=True, exist_ok=True)
    work = out / "spv"
    work.mkdir(exist_ok=True)

    # "all" is the default and the only safe value for a shipping build: the SDK
    # indexes the indirection table with the key it derives from the context
    # flags, and an unbuilt key is then read as a blob (garbage descriptor counts,
    # rejected layouts). A subset is useful only for debugging.
    keys = list(range(64)) if args.keys == "all" else [int(v) for v in args.keys.split(",")]
    selected = args.kits.split(",") if args.kits else list(KITS)

    manifest: dict[str, dict] = {}
    for kit_name in selected:
        kit = KITS[kit_name]
        root = sdk / "Kits" / "FidelityFX" / kit["root"]
        shaders = root / "internal" / "shaders"
        include = [root / path for path in kit["include"]]
        options = kit["keys"]
        pass_keys = list(range(1 << len(options))) if keys == list(range(64)) else keys

        for pass_key, filename in PASSES_BY_KIT[kit_name].items():
            forced = kit.get("forced", {})
            modules = []
            compiled_keys: list[int] = []
            for key in [k for k in pass_keys if k < (1 << len(options))]:
                defines = {name: 1 if key & (1 << bit) else 0
                           for bit, name in enumerate(options)}
                if any(defines[name] != value for name, value in forced.items()):
                    continue
                defines.update(forced)
                compiled_keys.append(key)
                blob = compile_pass(dxc, shaders / filename, include, defines,
                                    work / f"{kit['prefix']}_{pass_key}.{key}.spv",
                                    kit.get("call_macros"))
                reflection = reflect(blob)
                modules.append((blob, "CS", reflection))
                manifest[f"{kit_name}/{pass_key}@{key}"] = {
                    "size": len(blob),
                    "bindings": {k: [b for b, _ in v] for k, v in reflection.items()},
                }

            out_key = f"{kit['prefix']}_{pass_key}"
            (out / f"{out_key}_permutations.c").write_text(
                emit_pass(pass_key, modules, compiled_keys, kit["prefix"], options))
            # The SDK fills key.index = 0 then assigns one member per option and
            # indexes the indirection table with key.index. Only a union of the
            # whole word with the option bitfields makes index carry the options;
            # with separate int fields every lookup would read entry 0.
            names = kit.get("key_names", {})
            key_struct = [f"typedef union {kit['prefix']}_{pass_key}_PermutationKey {{",
                          "    struct {"]
            key_struct += [f"        uint32_t {names.get(option, option)} : 1;" for option in options]
            key_struct += ["    };", "    uint32_t index;",
                           f"}} {kit['prefix']}_{pass_key}_PermutationKey;"]
            (out / f"{out_key}_permutations.h").write_text(
                "// Generated by Tools/fsr3/build-shaders.py\n#pragma once\n"
                + "\n".join(key_struct) + "\n"
                "#include \"fsr3_shader_blob_header.h\"\n"
                + "".join(
                    f"extern const FfxShaderBlobHeader g_{out_key}{suffix}_PermutationInfo[];\n"
                    f"extern const int32_t g_{out_key}{suffix}_IndirectionTable[64];\n"
                    for suffix in ("", "_wave64", "_16bit", "_wave64_16bit")))
            # The shaderblobs translation unit includes the wave64 and 16-bit
            # tables by their own names; the modules are shared. The generic
            # modules are fp32, so a device that asks for fp16 gets correct output
            # with less through-put rather than a wrong image.
            for suffix in ("wave64", "16bit", "wave64_16bit"):
                (out / f"{out_key}_{suffix}_permutations.h").write_text(
                    "// Generated by Tools/fsr3/build-shaders.py\n#pragma once\n"
                    f"#include \"{out_key}_permutations.h\"\n")
            print(f"  {kit_name}/{pass_key}: {len(modules)} modules")

    (out / "manifest.json").write_text(json.dumps(manifest, indent=1))
    print(f"== wrote {sum(len(PASSES_BY_KIT[k]) for k in selected)} passes to {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
