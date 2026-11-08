# Reverse-engineering findings — Astro Bot PPSA21567

All facts below come from the files in `Game/PPSA21567-app` (content version 01.019.000, SDK 0x09000000,
requires system 13.20) via `Tools/self_info.py`, `Tools/self2elf.py`, `Tools/dyninfo.py`, and the
AnyPS5 runtime logs. Inferences are marked as such.

## Package contents

- `eboot.bin` (259 MB), `sce_module/libc.prx`, `sce_module/libSceNpCppWebApi.prx`, `sce_sys/` (param.json,
  trophies `trophy2/trophy00.ucp`, UDS stats, icons), `data/` (~160 GB, 166k files).
- `fakelib/` (`libSceAmpr.sprx`, `libScePlayGo.sprx`) and `ampr_emu.index` are **not part of the retail
  package**: they are artifacts of a firmware-backport tool (PS4-style fSELF magic `4F153D1D`, the
  [ampr_emu](https://github.com/drakmor/ampr_emu) AMPR I/O reimplementation). They are not used here;
  AnyPS5 provides its own `libSceAmpr`. A second copy under `backport (4.xx to 8.xx)/` in the archive was
  not extracted.
- `debuginfo.txt`: Perforce change 296393, git `85e47f1980` (2026-05-07), built by Team Asobi automation.
- Asset formats by count: `.anim` 61k, `.gnfp` 29k (GPU textures), `.odxb` 20k, `.jxm` 15k, `.szd` 13k
  (sound banks), `.pfbhk` 8.8k (Havok physics), `.skel`, `.jpx`, `.lvx` (levels), `.prefab`, `.mp4`
  (movies, via libSceAvPlayer), `.otf` fonts.

## Executable format

- PS5 SELF container, magic `54 14 F5 EE`, 12 entries: 6 segment blobs + 6 digest/extent tables.
  Entry flags are `signed | blocked` — **no encrypted or compressed bit**, segment entropy 3.1–6.9
  bits/byte (plaintext x86-64). The ELF is rebuilt by copying segments; no keys are involved.
- ELF64 x86-64, OSABI 9 (FreeBSD), `e_type 0xFE10` (ET_SCE_DYNEXEC = PIE executable), entry 0x70.
  14 program headers: text LOAD (RX, 117 MB at 0x0), rodata + `.eh_frame_hdr` (2.5 MB EH frame →
  C++ exceptions are used), RELRO data, **TLS** (0xA0 bytes), `SCE_PROCPARAM`, data/bss (BSS grows to
  0x6689410), a flags-0 LOAD holding dynamic linking data (strtab/symtab/rela), `DYNAMIC`,
  `SCE_COMMENT`, `SCE_VERSION` (library version table, 15.7 KB).
- PS5 uses **new dynamic tags** vs PS4: `0x61000043` (module info), `0x61000045` (needed module),
  `0x61000049` (import library), `0x61000041` (original filename). Symbols use standard `DT_SYMTAB`/
  `DT_STRTAB`; names are PS NIDs `xxxxxxxxxxx#L#M` (11-char base64 hash, library id, module id).
- Relocations: 569,024 `R_X86_64_RELATIVE`, 1,188 `R_X86_64_64`, 20 `GLOB_DAT`, 1,722 `JUMP_SLOT`.
- Module name of the executable: **`Armadillo_Prospero_Master`** ("Armadillo" = Team Asobi's engine,
  confirmed by runtime log lines `GAME: Armadillo Load end`; "Prospero" = PS5 codename).

## Runtime dependencies (1,747 imports from 61 modules, `Analysis/eboot_dyn.json`)

| Library | Imports | Role |
|---|---|---|
| libSceNpCppWebApi | 802 | PSN web API (C++), bundled in `sce_module` |
| libc | 212 | bundled Sony libc (Dinkumware-based) |
| libkernel / libScePosix | 115 / 21 | threads, memory (direct/flexible), files, events, time |
| libSceJson2 | 72 | JSON |
| **libSceAgc** / AgcDriver | 65 / 6 | **PS5 GPU API**: command buffers emitting AMD PM4 packets |
| **libSceAmpr** | 60 | PS5 async I/O (AMPR/APR command buffers, used by the streaming system) |
| libSceFont / FontFt | 52 / 2 | text rendering |
| libSceAudioPropagation, libSceAudioOut2, libSceAudiodec | 22 / 15 / — | 3D audio, output |
| libSceAvPlayer | 17 | video playback |
| libScePsml | 13 | PS5 ML upscaler ("MFSR" 09.40 per param.json) |
| libSceVideoOut | 11 | display/flip |
| libScePad | 10 | DualSense input (haptics are sample WAVs in `data/prein/haptics`) |
| libSceSaveData, Trophy2, UDS, NpManager, … | small | platform services |

## Observed runtime behaviour (AnyPS5, see `Logs/run-*`)

- Threading: the main thread starts a job system (12 workers at `0xeea550`), 11+ `Havok Worker` threads
  (`0x7ce260`), loader threads; ~40 threads early in boot.
- Memory: `kernel.flexibleMemorySize` = 343,932,928 bytes (328 MiB) from param.json; large direct-memory
  ranges (one 3.2 GB range at `0x1000000000`) are imported into Vulkan.
- Graphics: AGC command buffers produce PM4 packets (SET_SH_REG, DISPATCH_DIRECT, RELEASE_MEM,
  ACQUIRE_MEM, WAIT_REG_MEM_64, PUSH_MARKER with names such as `Compute…::Dispatch`). Shaders are RDNA2
  ISA (gfx10, wave32/64); 1,780 were recompiled to SPIR-V. Several use `s_swappc_b64` to computed targets
  (shader subroutines), which AnyPS5 prepares lazily at first use. Async compute queues 0x40/0x48 wait on
  graphics-queue labels via WAIT_REG_MEM_64 (current blocker).
- Level flow observed: `ps_logo` → `title_controller_ship` (title) → `worldmap` preload → `intro_next`
  → `hub_crashsite_tutorial`.

## Existing projects evaluated (source inspected, 2026-10-28)

| Project | Target | Finding | Decision |
|---|---|---|---|
| [AnyPS5](https://github.com/boykopovar/AnyPS5) (GPL-2.0, C++) | PS5 → native PE/ELF | Relinker + 368 system libs + AGC→Vulkan + shader recompiler. Its docs cite Astro Bot (PPSA21564/21567) dozens of times, incl. "Astro Bot at Crash Site on an RTX 5070 Ti". | **Base of this port.** Built from source, unmodified so far. |
| [SharpEmu](https://github.com/sharpemu/sharpemu) (GPL-2.0, C#) | PS5 emulator (HLE, native CPU) | Press reports Astro Bot reaching the PlayStation Studios logo in July 2026. | Reference only. |
| [shadPS4](https://github.com/shadps4-emu/shadPS4) | PS4 only | Useful for PS4-era libs (NIDs, pad, audio), not for AGC. | Reference only. |
| KytyPS5 | PS5 emulator | Cited by AnyPS5 for some AGC/AMPR signatures. | Not used. |

Why not a from-scratch loader: AnyPS5 already implements ELF loading, relocation, NID resolution, TLS,
exception unwinding, FreeBSD syscall trapping, and the AGC/Vulkan stack, and it boots this exact title.
Rebuilding these would only re-derive the same results; the useful engineering is in its open blockers.

## REA

REA (`morluto/rea`) is registered as an MCP server in Claude Code and connected. Its native-binary
analysis needs a Ghidra 12.1.x backend; Ghidra 12.1.4 and Temurin JDK 21 are installed in `Tools/` and
`GHIDRA_INSTALL_DIR`, `JAVA_HOME`, `GHIDRA_HEADLESS_MAXMEM=16G` were added to the REA registration in
`~/.claude.json` (backup in `REA/`). These take effect after the Claude session restarts; no REA function
analysis has been run yet. `Analysis/elf/eboot_etdyn.elf` is a copy with `e_type` patched to `ET_DYN` so
stock ELF loaders accept it.
