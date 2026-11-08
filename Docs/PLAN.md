# Plan: Astro Bot on this PC at 30+ FPS without graphical errors

Hardware: Ryzen 5 5500 (6 cores), RTX 5060 8 GB, 32 GB RAM, Resizable BAR off.

## How it works (the theory)

The PS5 and a PC share the same CPU type (x86-64, AMD Zen 2 vs. Zen 3 here). So the game's own code can run
directly on the Ryzen; that part is cheap. Everything around it has to be supplied:

1. **System libraries** (files, threads, memory, pad, audio, save data): rewritten for Windows. Cheap per call.
2. **Graphics**: this is the expensive part. The game talks to the PS5 GPU in AMD's native command language
   (PM4 packets) and AMD GPU shader code (RDNA2). A PC with an NVIDIA card needs every command translated to
   Vulkan and every shader recompiled to SPIR-V. The shaders are recompiled once and cached; the commands are
   translated live, ~600 draws and ~100 compute jobs per frame.
3. **Memory is the hard problem.** On the PS5 the CPU and GPU share one memory; the game freely lets the GPU write
   a buffer and the CPU read it a moment later, and vice versa. A PC has two memories (system RAM and 8 GB of VRAM)
   joined by a PCIe link. A translator must decide, for every piece of data, where the "real" copy lives and when to
   copy it across. Copy too eagerly: slow (what we measured: hundreds of MB per frame, ~800 GPU "stop and wait"
   barriers per frame, many CPU-waits-for-GPU syncs). Copy too lazily: wrong pictures (black tiles, missing effects).

So frame rate is decided mostly by how cleverly the translator handles memory, not by the GPU's raw power.
The game's own GPU work measured only ~18–55 ms per frame; AnyPS5's bookkeeping took the rest of ~110 ms.

## What exists already (researched 2026-10-28)

| Project | Approach | Astro Bot status |
|---|---|---|
| AnyPS5 (what we built) | Relinks the game to a Windows .exe; strict, safety-first memory model | Playable, ~20 FPS menu / ~9 FPS game on this PC, black tiles in one intro shot |
| **KytyPS5 – Jetsku U59 int21** | Emulator with a GPU-resident memory model, page-fault tracking, Astro-Bot-specific fixes, profile-guided build | Community completed the game at ~60 FPS most of the time (Ryzen 7 9800X3D + RX 9070 XT); release notes: Sky Garden at its 60 FPS cap, pirate island ~58 FPS (RTX 3090 class) |
| KytyPS5 – Bryan Adams fork | Same family | Crash Site 18 → 60 FPS, Sky Garden fly-in 5 → 25 FPS, cloud and planet artifacts fixed |
| SharpEmu | C# emulator | Reaches the PS logo only |

Sources: VideoCardz/Reddit (completion report), DSOGaming 2026-09-30, Jetsku/KytyPS5 README and release notes,
AnyPS5 discussions #357 and #11917.

## The plan

**Step 1 — Use what already works.** Run Astro Bot on KytyPS5 int21 (built by GitHub's public CI from commit
`744705e`, digest checked) on this PC. Measure FPS in the same places as before (title, Crash Site, worldmap,
intro fly-in) and check the pictures.

**Step 2 — Pick the faster base.** If Kyty is clearly faster and correct, it becomes the base and `Run-AstroBot.bat`
launches it. AnyPS5 stays as a fallback. (Expected: Kyty, because its memory model keeps data on the GPU and only
copies to the CPU when the CPU really touches it.)

**Step 3 — Tune for this PC.** The 8 GB card is the tightest constraint seen so far (VRAM ran full once). Use
Kyty's VRAM budget/garbage-collector settings, 1080p internal resolution, frame pacing; test with/without the
optional game patches (non-tiled lighting, GI off) for extra speed if needed.

**Step 4 — Fix what still breaks, with evidence.** For any wrong picture: find the failing pass (frame capture or
the emulator's own dumps), fix the translator, rebuild from source, re-test. Same for crashes.

**Step 5 — Own translator work, only where it pays.** Writing a new translator from zero would repeat years of
work in two existing GPL projects. The realistic "own translation" is to change the parts that limit this PC:
GPU-resident buffers instead of copy-back, fewer barriers (only real read/write hazards), no CPU waits on GPU
results the CPU never reads. Each change is measured A/B on the same scene before it is kept.

**User-side option:** enable Resizable BAR in the BIOS (lets the CPU write straight into VRAM; measured upstream
as ~35–40% less GPU time per frame in AnyPS5).

Goal checks: ≥30 FPS in menus and Crash Site/worldmap, no black tiles or missing effects in the scenes tested.
