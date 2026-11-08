# Astro Bot PC native port (AstroHybrid)

A personal research project: running my own legally owned PS5 copy of **Astro Bot** (PPSA21567, v01.019.000)
natively on Windows.

**No game files are in this repository**, and none will be: no executable, assets, shaders, keys, saves,
screenshots or SDK material. You need your own dumped copy of the game to use any of this.

## What it is

This is **not an emulator**. It is a native port:

- **AnyPS5** relinks the game's own x86-64 code into a Windows `.exe` (`AstroBot.exe`). The PS5 system
  libraries (kernel, libc, pad, audio, save data and so on) are reimplemented natively as Windows modules.
- **AstroHybrid**: the PS5 GPU side (AGC command buffers, shaders, VideoOut) is handled by a GPU stack taken from
  KytyPS5 and built as `kyty_gpu.dll`. It translates the GPU work to Vulkan and is loaded by the native exe.

Measured on a Ryzen 5 5500 + RTX 5060 8 GB: about 30-32 FPS in Sky Garden and 45+ in lighter areas. The CPU-side
GPU command processing is the limit.

## What is here

| Folder | Contents |
|---|---|
| `Source/patches` | AnyPS5 patch series (native libraries, AstroHybrid host, DualSense input/rumble/triggers) |
| `Source/hybrid/patches` | KytyPS5 patch series for the hybrid GPU DLL (frame-rate limits, pacing, fullscreen, real-time game speed) |
| `Source/game-patches` | Small optional byte patches applied at load time (lighting mode, GI probes, fixed 1080p) |
| `Tools/hybrid` | Launcher window (`launcher.ps1`), run-folder builder, reverse-engineering helpers |
| `Tests` | Benchmarks and A/B test scripts |
| `Docs` | Handover, plans, and the full results log (`OVERNIGHT.md`, every measurement good or bad) |
| `Play-AstroBot.bat` | Opens the launcher: lighting, frame-rate limit, window size, fullscreen, real-time speed |

## Notable findings

- **Slow motion below 60 FPS fixed (opt-in).** The game advances a fixed `1/FrameRate` per frame from one global
  (frame rate 60), so 30 FPS played at half speed. `ASTROHYBRID_GAME_SPEED=1` writes the measured frame rate there
  every present. Measured: 0.50x real time before, 0.98x after, at a 30 FPS cap.
- DualSense over Bluetooth: buttons, triggers, rumble, light bar and adaptive-trigger effects relayed to the pad.
- Steady frame pacing with frame-rate limits (30/40/45/60/120/unlimited), plus a persistent shader/pipeline cache.

## Status

Work in progress, personal use. Not affiliated with or endorsed by Sony Interactive Entertainment or Team ASOBI.
Astro Bot is a trademark of its owners.
