# ASTROHYBRID — PERFORMANCE ENGINEERING PROGRESS

## Completed Tasks
- [x] **Phase 1: Complete Read-Only Audit** (2026-11-18)
  - Inspected AstroHybrid architecture (`AstroBot*.exe` + AnyPS5 system `.prx` libraries + `astrohybrid_host.dll` + forwarder `.prx` shims -> `kyty_gpu.dll` built from KytyPS5 `u59-windows-20261024-int21` + 44 hybrid patches).
  - Audited all 30 AnyPS5 patches, 44 Kyty hybrid patches, and `GameSetup.cs` executable binary patches (NonRT lighting, SoftAssert, Cloth/ApCloth 60Hz clamp, CommandBuffer 8 MiB table & null-memcpy guards, 21:9/32:9 ultrawide, real-time `GameStepAddress`).
  - Reviewed upstream KytyPS5 (`u59-int21`, `biggsy-kyty` branch `astrobot-int21`), AnyPS5, and 2026-11-16 experiment logs (`Logs/run-20261116-*` and `Tools/hybrid/make_perftest.py`).
  - Established all 10 primary performance/stability bottlenecks and ranked proposed solutions.
- [x] **Phase 2: Batch 1 Engine Optimizations & Sky Garden A/B Benchmark** (2026-11-18)
  - Built `E:\AstroHybrid-Work\build\kyty-gpu-opt1\kyty_gpu.dll` on branch `perf/autonomous-60fps` and fixed `Run-AstroHybrid.bat` empty-string env overrides.
  - Benchmarked in `Build\RunHybridTest` (95s Sky Garden): CP commit **28.14 -> 19.82 ms/frame (-29.6%)**, CP wall **38.12 -> 31.01 ms/frame**, `1% low` **9.98 -> 14.98 FPS (+50.1%)**, `0.1% low` **2.73 -> 8.57 FPS (+213.9%)**, with **5.50 ms/frame CP idle** (now GPU-bound at ~31 ms).

## Key Discoveries
1. **Uncommitted Regression in `Run-AstroHybrid.bat` (Lines 15-17)**:
   - Working tree `Run-AstroHybrid.bat` sets `KYTY_BARRIER_BATCH=`, `KYTY_DISPATCH_OVERLAP=`, `KYTY_WAVE64_WARPS=` to empty strings.
   - In Kyty (`context.cpp:86`), `EnvSwitch("KYTY_BARRIER_BATCH", true)` returns `false` on empty string (`""`), which **disables the barrier batcher AND disables `KYTY_CP_RECORDER`** (`commandRecorder.cpp:235`: *"Kyty CP recorder: off (it needs the barrier batcher)"*), forcing all `vkCmd*` calls back onto the CP thread!
   - `LauncherService.cs` does NOT set those three variables, only `ASTROHYBRID_COMMIT_EPOCH_FASTPATH=0` and `KYTY_BUFFER_RANGE_FIXES=1`.
2. **Cloth/Blanket/Bubble Root Cause & `ASTROHYBRID_COMMIT_EPOCH_FASTPATH`**:
   - Cloth and blankets corrupt at <60 FPS when stepped with raw frame delta time (`dt > 1/60s`). `GameSetup.ApplyClothPhysicsPatches` clamps `ClothSimulation::get_dt_us` and `ApCloth` to `<= 1/60s` directly in `AstroBot*.exe`.
   - `ASTROHYBRID_COMMIT_EPOCH_FASTPATH=1` was disabled defensively during #30/#35 investigation, costing ~1.0-1.5 ms/frame of CP time (3,715,239 read acceptances & 2,022,771 writable acceptances verified with 0 mismatches). Furthermore, its read-only cross-epoch fast path (`ObtainReadBinding`) can be separated from writable buffer bindings if needed.
3. **`KYTY_BUFFER_RANGE_FIXES=1` Overhead**:
   - Enabled in `Run-AstroHybrid.bat` and `LauncherService.cs`, yet logs show **0 activations** in Astro Bot (`KYTY_BUFFER_RANGE_FIXES:` never prints). However, enabling it forces `PlanRanges` (`bindingPlan.cpp:87`) to check every descriptor and fall back to serial on zero-stride OOB=2 descriptors.
4. **Deferred EOP Label Writes Breaking DrawRun & Causing 612 Service Commands/Frame**:
   - In `graphicsRun.cpp:3410`, every deferred EOP label completion posts a `TrySendCommand` service lambda to the GPU thread.
   - `ProcessCommands()` (`graphicsRun.cpp:332-335`) runs between ops on the CP thread (~612 times/frame, costing 0.50-0.67 ms/frame directly) and unconditionally calls `SyncEpoch::Advance()` and `DrawRun::NoteForeignActivity("service command")`.
   - This causes **7,300-11,800 broken DrawRuns per 10s** (`activity by service command=9990`), forcing full 11.4 us DrawRun restarts instead of 3.6 us continuations (~2.5-3.0 ms/frame wasted on CP)!
   - Even worse: when a deferred label is already superseded (`gpu.DeferredLabelSuperseded(address, tick)` in `KYTY_LABEL_MODE=split`) or writes to a non-tracked/clean page, posting a service command and breaking `DrawRun` + advancing `SyncEpoch` is completely unnecessary.
5. **Command Buffer Submission Frequency & `DrawRun` Command/Activity Misses**:
   - `KYTY_IDLE_FLUSH_DRAWS=8` (32 in pass), `KYTY_EOP_FLUSH_BATCH=64`, `KYTY_GFX_SLICE_DRAWS=128`, `KYTY_SUBMIT_MIN_INTERVAL_US=0` cause ~124 command buffer flushes per frame and ~8,500 `command` misses + ~3,300 `submission resume` misses per 10s in `DrawRun`.
6. **Hardware Thread Contention on Ryzen 5 5500 (6C/12T, 16MB L3)**:
   - `KYTY_CPU_RESERVE=cp` reserves 1 physical core (2 threads) for the CP.
   - The remaining 5 cores (10 threads) host: 12 game TBB workers + 8 background threads + 8 Havok threads + 6 DrawPrep workers (2 hot spinning at Highest priority) + 1 CP Sequencer thread + 1 CP Recorder thread + audio/present threads (>38 active threads on 10 logical processors).
   - Game threads and CP recorder/sequencer contend with DrawPrep workers (`KYTY_DRAW_PREP_PRIORITY=2`).
7. **Shipped `kyty_gpu.dll` Build Configuration (`build_kyty_gpu.py`)**:
   - Even when CMake configures `-DKYTY_EMULATOR_IPO=ON`, `build_kyty_gpu.py` compiles `kytyGpuBridge.cpp`, `memory.cpp`, `dualSenseBluetooth.cpp`, `dualSenseHaptics.cpp` without ThinLTO/IPO flags or PGO flags if not present in `agc.cpp.obj`'s block, and links objects without cross-module LTO codegen check. Also `C:` drive has 25.05 GB free (below the 30 GB threshold in `build_gpu_v3.cmd`).
8. **Dynamic Resolution & Internal Render Passes (`make_perftest.py` findings)**:
   - On RTX 5060, GPU frame time in Sky Garden is ~18-23 ms at 1080p internal (or ~31-39 ms with DLSS-G + 1440p output), while CP commit time is ~23-25 ms for ~5,650 draws/frame.
   - In `2026-11-16` PerfTest runs, reducing draw count from ~5,650 to ~1,950-2,150 draws/frame dropped CP commit from ~26.5 ms to **13.0-16.2 ms/frame** (reaching **39-45.5 native FPS** in Sky Garden!).
9. **Page Protection / Write-Tracking Fault Cost**:
   - Sky Garden triggers 100-170 write faults/frame (`VirtualProtect` + handler taking 50-180 us/fault = 7-20 ms/frame summed across guest threads) plus 27-37 protect calls/frame on CP (0.5-0.6 ms/frame). `ASTROHYBRID_OPT_1=1` and `ASTROHYBRID_GUEST_LOAD_FAULTS=1` are coded in `kyty_gpu.dll` but not enabled in the launcher/batch script.
10. **Blackwell / Shader Codegen (`KYTY_FLOAT_PROFILE=relaxed` from `biggsy-kyty`)**:
    - Upstream `biggsy-kyty` adds `KYTY_FLOAT_PROFILE=relaxed` (1 host instruction per guest min/max/med3/pack/sin/cos/cvt instead of multi-instruction NaN/denorm emulation sequences), reducing GPU shader execution time while keeping `GeometryOutputGuardAutomatic(Blackwell) == false`.

## Modified Files
- None yet (Phase 1 is strictly Read-Only Audit).

## Next Actions
1. Present Read-Only Audit report.
2. Restore/fix `Run-AstroHybrid.bat` uncommitted lines (`KYTY_BARRIER_BATCH`, `KYTY_DISPATCH_OVERLAP`, `KYTY_WAVE64_WARPS`) so `KYTY_BARRIER_BATCH=1` stays active.
3. Benchmark baseline in isolated `Build\RunHybridTest` and test high-impact CP / DrawRun / EOP service-command & submission batching optimizations.
