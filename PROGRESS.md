# Progress — Astro Bot (PPSA21567 v01.019.000) on Windows

Approach: **native relinking, no CPU emulation.** The PS5 runs x86-64 (Zen 2), so the game's own code
runs directly on the Ryzen 5 5500. [AnyPS5](https://github.com/boykopovar/AnyPS5) (GPL-2.0, rev `2199ee0`)
converts the PS5 ELF into a Windows PE and supplies reimplemented PS5 system libraries (`libs/*.prx`,
368 built), including an AGC → Vulkan graphics driver and an RDNA2 → SPIR-V shader recompiler.
This project builds on it rather than duplicating it (see `Docs/REVERSE_ENGINEERING.md` for why).

## Milestones (evidence from 2026-10-28 runs)

| # | Milestone | Status | Evidence |
|---|-----------|--------|----------|
| 1 | Environment ready | ✅ | Archive listed/extracted with 7-Zip exit 0; 159,849,464,460 bytes out; 80.6 GB free after. GCC 15.2.0 (WinLibs UCRT), CMake, Ninja, Python 3.15, Git, JDK 21, Ghidra 12.1.4 installed under `Tools/`. |
| 2 | Game analyzed | ✅ | `Docs/REVERSE_ENGINEERING.md`; imports dumped to `Analysis/eboot_dyn.json` (1,747 imports, 61 modules). |
| 3 | Executable loading | ✅ | `Logs/relink.log`: "Conversion complete", 2,930 external prx references, 2 bundled modules converted. |
| 4 | Runtime initialization | ✅ | Engine log: `GAME: Resident Load end`, `Armadillo Load end`, 30+ worker threads (Havok, job system), save data, trophies parsed. |
| 5 | Graphics | ✅ | Vulkan device on RTX 5060 (`Logs/run-*/stdout.log`), swapchain, 1,780 shaders recompiled and disk-cached. |
| 6 | First frame | ✅ | `Logs/run-20261028-180804/shot_0060s.png` ("Sony Interactive Entertainment presents"). |
| 7 | Interactive menu | ✅ (partial) | `shot_0420s.png`: ASTRO BOT title, "PRESS ANY BUTTON". Run `run-20261028-181557`: injected Return (= Cross) moved title → `intro_next` (intro cutscene) → `hub_crashsite_tutorial` loaded. |
| 8 | Gameplay | ✅ (partial) | `Logs/run-20261028-191958/shot_0250s.png`–`0600s.png`: Astro in the Crash Site desert, input moves/jumps him, rendering correct, 8.3–8.8 FPS. Worldmap via `-skipIntro` renders correctly (`run-20261028-195727`). Ship cutscene still has black tiles. Audio not verified. |
| 9 | Optimization | ⏳ in progress | Menu ≈10 → ≈18 FPS, game 7.2 → 8.8 FPS from two runtime settings (`Docs/PERFORMANCE.md`). 30 FPS needs driver architecture work (AnyPS5 #7895). |

## The fix that unblocked boot

Runs 1–2 aborted with **0xC0000409 (−1073740791)** — the same code from the earlier AnyPS5 experiment.
Decoded from stderr, not guessed:

```
[gpu] host import budget: 8059 MiB live of 8159 MiB ...; 3 imports (6099 MiB) refused
what(): guest allocation 0x336aa4000+0x4000 is still pinned by GPU work or an import (waited 60000 ms ...)
FATAL: abort() (SIGABRT)
```

AnyPS5 throws `std::runtime_error` on unsupported states; `abort()` under MinGW/UCRT ends the process
with `STATUS_STACK_BUFFER_OVERRUN` (0xC0000409, the `__fastfail` code), so this exit code means
"runtime gave up", not a memory-corruption bug in the game. The stuck lease lies inside the 1.4 GB
import that was refused for budget (`0x324614000+0x599ec000`). Raising `APS5_HOST_IMPORT_MIB` to 11264
(inside the 15,550 MiB host heap budget the NVIDIA driver reports, 32 GB RAM) removed the abort:
run 3 ran the full 420 s and reached the title screen. This is a configuration fix; the underlying
issue (a refused import's fallback path can leave a lease pinned) is still an upstream bug — see
`KNOWN_ISSUES.md`.

## Runs

| Run (Logs/) | Config | Result |
|---|---|---|
| `run-20261028-180155.*` | defaults | abort 0xC0000409 after pinned-lease wait; 105 flips, black screen |
| `run-20261028-180607` | `APS5_OUTPUT_RESOLUTION=2560x1440` | same abort at 87 s |
| `run-20261028-180804` | + `APS5_HOST_IMPORT_MIB=11264` | no crash in 420 s; PS logo → title screen |
| `run-20261028-181557` | + Return keypresses after title | title → intro cutscene → crash-site hub loaded; stopped by harness at 540 s |
