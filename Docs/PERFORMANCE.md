# Performance measurements

Hardware (verified): Ryzen 5 5500 (6C/12T, Zen 3), RTX 5060 8 GB (NVIDIA 621.01), 31.9 GB RAM.
Window 1484×835; the game always registers a 3840×2160 swap chain (it reads
`sceVideoOutGetOutputStatus` but keeps 4K output buffers and scales internally).
Frame numbers come from `APS5_TRACE_SUMMARY_EVERY=10` digested with `tools/trace_digest.py`.

## Results by configuration (2026-10-28)

| Run | Settings beyond `APS5_HOST_IMPORT_MIB` | Title/menu | Gameplay |
|---|---|---|---|
| baseline `run-20261028-185903` | import 11264, profiling on | 88–109 ms (≈10 FPS) | ship cutscene 138 ms (7.2 FPS) |
| B `run-20261028-191958` | + `APS5_NO_LABEL_READBACK_STORE=1` | 55 ms (≈18 FPS) | Crash Site desert 120 ms (8.3 FPS) |
| C `run-20261028-193922` | + import 14848 (no refusals) | — | 113 ms (8.8 FPS) |
| D `run-20261028-194841` | + `APS5_DRAW_PIPELINE=1` (inactive: profiling disables handoff) | — | 113–117 ms |
| E `run-20261028-195727` | C settings + draw pipeline, profiling **off**, `-skipIntro` | — | worldmap 114 ms (8.8 FPS), GPU busy 101 ms |
| H `run-20261028-211917` | **updated build** (main 319621ba2 + PR #9712) + `APS5_DEPTH_PLANE_COPY_MIB=512` | 50 ms (≈20 FPS) | interior cutscene collapses to 0.5 FPS: VRAM full (6.5/7.0 GB) |
| I `run-20261028-213001` | updated build, default depth budget | 50–58 ms (≈18–20 FPS) | interior cutscene 114–141 ms (7–8.8 FPS) |
| S `run-20261028-233154` | **Resizable BAR on** (BAR1 8192 MiB), same settings as I | 43–53 ms (≈19–23 FPS) | UFO cutscene 83–100 ms (≈10–12 FPS, was 7.0–7.5); GPU busy 61–80 ms (was 100–120) |
| T `run-20261028-234230` | Resizable BAR, world map (`Tests/worldmap_bench.ps1`) | — | world map 112 ms (8.9 FPS), GPU busy 86 ms (was 101): no longer GPU-bound |
| U `run-20261028-234823` | T + `APS5_BATCH_CAP=256` `APS5_LABEL_FLUSH_US=4000` | — | 95–124 ms, two stalled windows; no gain, not kept |
| X `run-20261029-000701` | T + **`APS5_PIPELINED_DRAWS=64`** (hand-off queue 8 → 64) | — | world map **100 ms (10.0 FPS)**, GPU busy 71 ms: +12%, kept |
| Y `run-20261029-001218` | `APS5_PIPELINED_DRAWS=256` + `APS5_PIPELINE_DISPATCH=1` | — | 100–104 ms: no better than X |
| Z `run-20261029-001748` | X + `ANYPS5_PRESENT_MODE=mailbox` | — | 100–104 ms: no gain (game paces to its own 60 Hz vblank; frames land on 6-vblank slots) |

`Run-AstroBot.bat` uses import 14848 + `APS5_NO_LABEL_READBACK_STORE=1` on the updated build.
The bring-up thread (AnyPS5 discussion #357) reports `APS5_TEXTURE_CACHE_MIB=2816` taking Sky Garden from
9–10 to 28–31 FPS on a 12 GB RTX 3080 Ti; on this 8 GB card the texture cache already sits above its
2 GB floor and VRAM is the limit, so that setting is not usable here.

## Where a gameplay frame goes (worldmap, run `20261028-200349`, `APS5_PROFILE_GPU_DRAWS`)

- Frame ≈ 100 ms; GPU busy ≈ 83 ms; measured GPU work 55 ms.
- One compute program (guest `0x555f41f00`, once per frame) **grows from 2 ms to 9 ms per frame** during
  the session; draw variant `0x8a1` (160 draws/frame) 7 ms; host helper shader `0x25` (220/frame) 4.6 ms.
- Emulation overhead on the GPU timeline: staging copies in/out 12 ms, shadow publish 5 ms, write-backs and
  uploads ~2.5 ms, ~800 pipeline barriers per frame (the hazard validator, `APS5_BARRIER_VALIDATE`, finds
  69% of them unneeded).
- CPU: the whole process uses only 1.5–2.5 of 12 hardware threads; the queue-0 worker ~25–50% of a core.
  The frame is latency-bound by CPU↔GPU syncs (`CompletePendingCpuWrites` alone: 51 s of waits in 8 min).

## Why 30 FPS in game is not reachable by tuning

Every remaining cost is structural to AnyPS5's GPU memory-coherence model (staging copies, write-back
round trips, barrier-per-pass recording, CPU syncs on copied writes). Upstream issue
[#7895](https://github.com/boykopovar/AnyPS5/issues/7895) tracks the same queue-0 bottleneck; experiments
there (parallel prepare, dispatch pipelining, label-wait changes) gained 1–5% each. Another Astro Bot tester
([#11917](https://github.com/boykopovar/AnyPS5/discussions/11917), RTX 4050 + i7-13700HX) reports 15–16 FPS
on the title screen and "heavy queue lag" in game, matching these numbers.

## Hardware notes (verified 2026-10-28)

- Resizable BAR enabled by the user in BIOS (BAR1 256 MiB → 8192 MiB; Vulkan memory type 4 = device-local +
  host-visible on the 7.9 GB heap). AnyPS5 patch 0001 (PR #9712) then places draw snapshots in VRAM.
- The GPU link runs at **PCIe 3.0 x8** (`nvidia-smi pcie.link.gen.max 3, width 8`): the Ryzen 5 5500 only
  offers PCIe 3.0, and the RTX 5060 uses 8 lanes, so host↔VRAM bandwidth is about half of what the card is
  designed for. Copy-heavy translation suffers more on this PC than on PCIe 4.0 systems.

## Sampling profile of the draw path (world map, Resizable BAR, `APS5_SAMPLE_COMMITTER/WORKER`, run W)

- Draw recorder (DrawPipeline::run): ~43% idle (condition wait + spin with steady_clock reads), Graphics::Draw
  48% (prepareDraw 28%, recordPreparedDraw 20%), guest write tracking (GetWriteWatch collect) ~10%, draw
  snapshot refresh ~9%.
- Queue-0 worker: blocked 23.5% in DrawPipeline::Enqueue with the default 8-slot queue (fixed by X),
  materializeDrawStage 19%, dispatches 16%, label waits 11.5%, ShaderResources::Complete 10%.
- Per draw (APS5_PROFILE_DRAW): worker 63 us (lock wait 23, capture 13, shader key 12), recorder 66 us
  (record 25, lookup 15, readTarget 7). ~777 draws per world-map frame.
