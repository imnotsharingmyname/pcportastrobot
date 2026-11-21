# ASTROHYBRID — PERFORMANCE BENCHMARKS

## Hardware & Test Environment
- **CPU**: AMD Ryzen 5 5500 (6 Cores / 12 Threads, 16 MiB L3 Cache, Zen 3 Cezanne)
- **GPU**: NVIDIA GeForce RTX 5060 8 GB (Blackwell `0x2d05`, Driver 621.01, Vulkan 1.4.357, PCIe 3.0 x8, ReBAR Off / 214 MiB host-visible device-local heap)
- **RAM**: 32 GB DDR4
- **Game Version**: Astro Bot `PPSA21567` v01.019.000 (`AstroBot-NonRT-SoftAssert.exe`)
- **Note**: All FPS figures below are **true native rendered game FPS** (excluding DLSS Frame Generation interpolated frames).

---

## Historical Baselines (Existing Valid Measurements)

| Date / Run ID | Scene / Scenario | Configuration | Native Avg FPS | 1% Low FPS / p99 | Frame Time (ms) | CP Commit (ms) | Draws / Frame | Notes |
|---|---|---|---|---|---|---|---|---|
| `2026-11-06` AnyPS5 | Sky Garden (Demanding) | AnyPS5 Vulkan backend (patches 0001-0020) | 1.2 - 1.3 | < 1.0 | ~780 ms | N/A | ~4,800 | VRAM spillover (651 ms GPU/frame) |
| `2026-11-06` Kyty M4 | Sky Garden (Demanding) | Initial `kyty_gpu.dll` transplant | 29.2 | ~20.0 | 34.2 ms | ~22.5 ms | ~4,800 | First working hybrid build |
| `2026-11-07` `042131` | Sky Garden (Pinned Idle) | + `KYTY_HANG_WATCHDOG=0` + DrawPrep Priority/Spin (`1/10/5`) | 31.6 (30.5-32.7) | 21.9 (42.0-45.4 ms p99) | 31.6 ms | ~21.8 ms | ~4,850 | +4.17% mean FPS, +12.37% 1% low |
| `2026-11-08` `013926` | Sky Garden (Pinned Idle) | + `ASTROHYBRID_CACHE_TITLE_ID=PPSA21567` (Warm Cache) | ~32.5 | ~22.5 (50.5 ms worst vs 103.7 ms cold) | 30.8 ms | ~21.5 ms | ~4,850 | Eliminated 100+ ms shader compile hitches |
| `2026-11-12` `015906` | Hoover Beach (Lightweight/Mod) | Standard PGO `kyty_gpu.dll` (`1280x720`, uncapped) | **51.0 - 53.0** | ~42.0 | 19.2 ms | ~11.5 ms | ~2,200 | Start area steady state |
| `2026-11-12` `020113` | Snowy Canyon (Moderate) | Standard PGO `kyty_gpu.dll` (`1280x720`, uncapped) | **43.1 - 44.3** | ~35.0 | 22.8 ms | ~14.5 ms | ~2,900 | Start area steady state |
| `2026-11-12` `020321` | Cymbal Party Land (Mod-Heavy) | Standard PGO `kyty_gpu.dll` (`1280x720`, uncapped) | **37.7 - 38.5** | ~30.0 | 26.2 ms | ~17.5 ms | ~3,500 | Start room steady state |
| `2026-11-16` `134831` | Sky Garden Underwater/Foliage (Very Demanding) | `AstroBot-NonRT-SoftAssert.exe`, `COMMIT_EPOCH_FASTPATH=0`, `BUFFER_RANGE_FIXES=1` | **29.5 - 31.6** | ~22.0 | 31.66 - 33.94 ms | 23.18 - 24.84 ms | 5,093 - 5,659 | Current v1.2.x baseline in heaviest foliage area (GPU time 31.6 ms) |
| `2026-11-16` `025146` | Sky Garden Underwater/Foliage (PerfTest pruned) | `AstroBot-PerfTest.exe` (reduced secondary pass draws: 1,697 draws/f) | **45.5** | ~36.0 | 22.00 ms | **13.01 ms** | 1,697 | Proves CP commit scales linearly with draw count + EOP gap (8.99 ms) |

---

## Optimization Experiments Log

### Run 1 — Clean v1.2.4 Baseline (`Build\RunHybridTest`, 95s Sky Garden)
- **Log**: `Logs\run-20261118-172709\stdout.log`
- **Frame Times CSV**: `Logs\frame_times_baseline_skygarden.csv` (2752 frames)
- **Steady-State Gameplay (frames >600)**:
  - Average frame time: **33.46 ms (29.89 FPS)**
  - 1% low: **9.98 FPS (100.19 ms)**
  - 0.1% low: **2.73 FPS**
- **Steady-State `CpGaps 10s` (last 10s window)**:
  - Wall: **38.12 ms/frame (263 frames / 10s)** | Commit: **28.14 ms (4756 draws/f)** | Gap: **9.98 ms** (`headwait.deep 3.706 ms`, `idle 0.189 ms`)
- **Steady-State `DrawRun 10s`**:
  - `activity by service command=7988`, `submission resume=2645`, `continued=72.9%`

### Run 2 — Batch 1 Engine Optimizations (`Build\RunHybridTest`, 95s Sky Garden)
- **Changes**: Quiet deferred EOP label/interrupt service commands + superseded-label skip (`graphicsRun.cpp`), `ASTROHYBRID_COMMIT_EPOCH_FASTPATH=read` (read-only clean memo fastpath ON, writable OFF), `ASTROHYBRID_OPT_1=1` & `ASTROHYBRID_OPT_2=1` default-on, `MemoryTracker::TakeGpuPagesForCpu` (`memoryTracker.cpp`), `KYTY_LABEL_HANDOFF` (`graphicsRun.cpp`), DLSS/FG per-draw fast-exit guards (`renderDraw.cpp`), Streamline warn suppression (`frameGen.cpp`), `KYTY_DRAW_PREP_WORKERS=6`, `KYTY_SUBMIT_MIN_INTERVAL_US=500`, `KYTY_GFX_SLICE_DRAWS=512`.
- **Log**: `Logs\run-20261118-174335\stdout.log`
- **Frame Times CSV**: `Logs\frame_times_opt1_skygarden.csv` (2816 frames)
- **Steady-State Gameplay (frames >600)**:
  - Average frame time: **33.50 ms (29.85 FPS)** (GPU-bound at ~31 ms; CP now has **5.50 ms/frame idle**)
  - 1% low: **14.98 FPS (66.77 ms)** (**+50.1%**)
  - 0.1% low: **8.57 FPS** (**+213.9%**)
- **Steady-State `CpGaps 10s` (last 10s window)**:
  - Wall: **31.01 ms/frame (323 frames / 10s)** (**+22.8% CP throughput**) | Commit: **19.82 ms (4811 draws/f)** (**-29.6%**) | Gap: **11.19 ms** (`idle 5.497 ms`, `headwait.deep 0.269 ms`)
- **Steady-State `DrawRun 10s`**:
  - `activity by service command=3` (**-99.96%**), `submission resume=1245` (**-52.9%**), `continued=74.4%`
