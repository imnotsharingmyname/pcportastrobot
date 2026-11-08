# Handover - verified state on 2026-10-30 (Codex)

Read root AGENTS.md and Tools/AnyPS5/AGENTS.md before editing. This is a native Windows port, not an
emulator. Game/ and E:\PPSA21567.7z were not modified. Keep at least 30 GB free on C:, preserve junctions,
never discard someone else's work, and do not push. All new commits are local.

## Current working state

- 2026-10-30 11:00 (Claude): `Tests\skygarden_bench.ps1` (Sky Garden = `-lvl underwater_aerial_garden`, ~110 s).
  Patches 0017 (`APS5_SAMPLED_BUDGET_FLOOR_MIB`, launcher 3584) and 0018 (recompiler call_once fast path).
  Sky Garden ~1.3 FPS (2.5 with `APS5_DEPTH_PLANE_COPY_MIB=128`, which breaks the world map). Next patch 0019.

- 2026-10-30 10:05 (Claude): patch 0016 `APS5_SYNC_COMPLETION_LABELS_ONLY` (AnyPS5 `0ed7b2ba4`), on in
  Run-AstroBot.bat and the benchmark helpers: ~+4.2% (134-135 -> 140-142 presents/10 s). Next patch is 0017.

- Current AnyPS5 checkpoint: `3a298ddb0787`, clean. Patches 0001-0015 are exported under Source/patches.
  Patch 0015 only extends allocator test coverage; the verified runtime optimization remains 0014.
- New 0013 (`f735deec7`): bounded per-resource storage traffic diagnostics. Engine switch
  `APS5_PROFILE_STORAGE_RESOURCES=1`, OFF by default; also requires `APS5_PROFILE_DRAW=1`.
- New 0014 (`f39549507`): aligned thin linear storage images can use existing block-unit VRAM shadows.
  `APS5_LINEAR_BLOCK_TRACKING` remains OFF by default in the engine, but is now ON in Run-AstroBot.bat and
  the benchmark helpers after three positive A/B pairs. Set it to `0` to restore the earlier path.
- Installed libSceAgcDriver.prx was rebuilt after the final commit and matches the build-tree driver:
  SHA-256 `C996BA3953BD5E8D03BEE929B5D0A42F9EF270DF7E526B78D3923B0804811BF8`.
  Build log: `Logs/build-checkpoint-20261030-034356.log`.
  The earlier verified 0014 runtime (SHA 40ABE55D...) is preserved as a fallback in
  `work/libSceAgcDriver.linear-default.prx`; current runtime behavior is the same, with final trace metadata.
- World-map A/B: about 12.8-13.1 FPS disabled versus 13.2-13.4 enabled; about 2.45% gain using means of
  statistical run medians. This is a small measured gain. The 30/60 FPS targets remain unfinished.
- Reclaim experiment was rejected and preserved before restoring Recompiler.cpp to HEAD. See below.
- Allocator fast-free experiment was marginal and rejected; original HostAllocator.cpp restored. Only its
  expanded sized-delete regression test was kept. Both rejected runtime diffs are in Source/experiments.
- Committed-build verification `run-20261030-030620`: 13.4 FPS steady, last GPU busy 53.0 ms, screenshot
  correct. CPU sample details below. Telemetry `smi-modes-20261030-030620-430-1.csv`.
- Final restored verification `run-20261030-034543`: 13.2 FPS, last GPU busy 53.9 ms / idle 22.1 ms,
  screenshot correct, clean `3a298ddb0787` trace; `Logs/final-20261030-digest.txt`. No game process remains;
  app0 points to original Game, C: free ~85.5 GiB. No unfinished source edits are required for this build.

## What caused the measured GPU variation

The observed slow storage mode is localized to repeated write-back/re-upload between overlapping linear
storage-image views, rather than a general GPU clock slowdown. In slow run `run-20261030-023028`, same-base
linear 10240x320 format-71 one-layer/array descriptors at `0x556970000` repeatedly move ~50 MiB/frame out
and another ~50 MiB/frame back in. These transfers are absent from fast steady top groups. Both addresses
are aligned: the earlier alignment-only hypothesis was insufficient; linear layout was excluded from the
block-unit path. The new switch reuses that path with linear rows represented as one-element blocks.
It preserves existing write stamps, unit shadows, padding handling and recorder resource lifetimes.

Earlier slow capture `run-20261030-020809` had median GPU busy 73.25 ms versus ~55.7 fast. Per frame:
storage-upload 9.92 ms and storage-writeback 10.55 ms versus ~1.0/1.6 fast. Draws (~21.5 ms), compute
programs and staging-out (~9.8 ms) stayed normal. GPU telemetry: median 6138 MiB, SM 2715 MHz, memory
14001 MHz, P0, 52.8 W, 49.2 C. No allocator fallback was logged. Simple global downclock/thermal or
VRAM-full explanations are not supported by this capture. This does not prove every future slow mode has
the same cause. WHY those views remain active in only some runs is still not fully explained.

## Linear block-shadow measurements and validation

All six A/B runs used the same experimental binary, common GPU/resource profiling and flushed telemetry.
Round two reversed test/base order. Names below have prefix `run-20261030-`.

| Round | Base folder / presents per 10 s | Enabled folder / presents per 10 s | Median GPU busy, base / enabled |
|---|---|---|---|
| 1 | 024702 / 129 | 024935 / 134 | 55.9 / 53.6 ms |
| 2 (test first) | 025440 / 131 | 025208 / 132 | 55.5 / 53.5 ms |
| 3 | 025713 / 128 | 025946 / 132 | 55.7 / 53.7 ms |

`presents.py` uses its original upper-middle median. Statistical medians are base 129/130.5/128 and test
133.5/132/131.5. The ratio of their means is +2.45%. JSON:
`Logs/linear-ab-20261030-024701.json`. Telemetry: six `smi-ab-20261030-024701-730-*.csv` files.
First pair class medians (ms/frame): storage-upload 0.98 -> 0.29; storage-writeback 1.59 -> 0.88;
shadow-publish 0.77 -> 0.16; draws unchanged. Saved trace_digest summaries:
`Logs/linear-ab-20261030-base1-digest.txt`, `Logs/linear-ab-20261030-test1-digest.txt`.
All three enabled final screenshots were opened and checked (third is shot_0151s.png): correct world map.
No claim yet that the new path eliminates every occurrence of the slow mode: the A/B controls were fast.

Focused tests on the same RTX 5060, with REQUIRE_VULKAN, NO_SHADER_CACHE and Vulkan validation:

- `Logs/linear-tests-baseline-20261030-023936`: 3/3 passed.
- `Logs/linear-tests-experimental-20261030-024121`: 3/3 passed; 0 VUID/missing-layer messages in either log.
- Pattern: `^agc_storage_(upload_lifetime|write_back|writeback_blocks)$`. Storage move tests now exercise
  14 surface cases in both memory modes, including four new varied linear cases (padding, partial rows,
  multiple mip levels). Initial baseline `023717` only failed the tiled-specific minimum move count:
  linear legitimately uses two instead of three; corrected, then reran both versions successfully.
- `run-20261030-024313`: 180 s enabled world-map validation; 90/180 s screenshots correct, no reported
  VUID/missing-layer errors. `Logs/validation-linear-20261030-0243.txt` is empty. Used the handover's known
  blend-state 00605 filter for this runtime check; focused tests used no such filter. Validation's ~5.5 FPS
  is not comparable with ordinary runs; GPU busy stayed ~54-56 ms.

Diagnostic-only earlier tests also passed 3/3: `storage-tests-baseline-20261030-022418` and
`storage-tests-diagnostic-20261030-022658`. Do not count `022631`: copying a source snapshot preserved an
old timestamp and Ninja reused the baseline object. Corrected by touching the selected source, then
verified an actual Texture.cpp compile and the new switch in the binary before deployment.

## Reclaim experiment resolved

Claude's original uncommitted diff is tracked at `Source/experiments/APS5_RECLAIM_RESULTS.patch`, root commit
`2c1d8a8`; it is outside the auto-applied patch directory. Full original source is also in
`work/Recompiler.reclaim.cpp`, original diff backup in `work/reclaim-results-original.patch`.
Restoration to HEAD happened only after checking the original file's SHA-256 and preserving the diff.

Three crossover A/B rounds: equivalent fast-mode test/base 129/131 (`021315`/`021547`), then base/test
131/131 (`021820`/`022053`) presents/10 s. First pair base/test 106/132 (`020809`/`021042`) crossed GPU
storage modes and cannot support a reclaim claim. No repeatable benefit; do not re-enable by default.
Enabled screenshots were correct. Results: `Logs/reclaim-ab-20261030-020808.json`, six corresponding
`smi-ab-20261030-020808-727-*.csv`. Old example runs `011627`/`011900` also differed in reclaim and contain
no gputime lines; they cannot settle causality.

## CPU profile and next priorities

Current clean-build samples in `run-20261030-030620/threads_0090s.txt` and `threads_0120s.txt`: queue-0
71.2% / 63.1% of one core; unnamed tid 15232 73.8% / 74.1%; process total 2.23 / 2.41 cores. Do not label
the unnamed thread without a stack. Final draw interval: 227310 commits at 31.5 us each; 7352 packet
drains taking 2520.6 ms over 10 s (~55 drains/frame, mainly DISPATCH_DIRECT 0x15). GPU idle gaps remain
~20-22 ms. Use actual thread CPU samples as well as packet timing: wall spans include waits.
Trace digest: `Logs/current-cpu-20261030-digest.txt`. A deeper existing worker/committer sampler run is now
complete. `run-20261030-031115` is whole-run only (manual earlier copy was late). Automatically windowed
`run-20261030-031516` has raw/start/symbolized captures and summaries under
`Logs/samples-codex-20261030-steady/`, 5124 samples/thread. Worker: Drain 24.5% inclusive,
materializeDrawStage 25.9%, heap-size query ~7.1% self with sized-delete parents. Committer: commitDraw
59.7%, prepareDraw 31.0%, kernel wait ~25.1%. QPC/clock polling is waiting, not proof of wasted work.
The original loaded AGC module is preserved as `modules/libSceAgcDriver.prx` under that capture directory
(SHA 40ABE55D...). For repeat symbolization, add that modules directory to --libs alongside Build/Run/libs
and C:/Windows/System32. Existing symbolized outputs were verified against matching module headers.

Allocator experiment resolved: `APS5_DIRECT_SIZED_FREE` bypassed heap-size queries for sized frees known
to be too large for any cache bucket. Baseline `allocator-tests-baseline-20261030-032317` and enabled
`allocator-tests-experimental-20261030-032533` each passed 4/4 tests, with no VUID/missing-layer messages.
Three same-GPU-mode crossover pairs gave base/test 132/132 (`032722`/`032956`), 133/134 (`033501`/`033228`,
test first), 133/134 (`033734`/`034007`) presents/10 s. Only ~0.5% in means, a tie and one-frame differences:
not a convincing material gain, so runtime change rejected. Source restored after preserving
`Source/experiments/APS5_DIRECT_SIZED_FREE.patch` in root commit `b5a332c`. JSON:
`Logs/allocator-ab-20261030-032722.json`; six `smi-ab-20261030-032722-355-*.csv` files. First enabled final
screenshot correct. Kept explicit sized-delete coverage through 4096 bytes as engine `3a298ddb0`, patch
0015. Final allocator test rebuilt against restored source and passed:
`Logs/host-allocator-final-build-20261030.log`, `Logs/host-allocator-final-test-20261030.log`.
No allocator fast-free default was added. Do not repeat this test without evidence beyond export-nearest
Windows heap symbols; those labels are approximate and the frame-level result is the deciding measurement.

1. Read the new CPU samples and pick one measured queue-0 / committer / mutex bottleneck.
2. Check long-run storage-mode stability with linear shadows enabled; investigate why the linear aliases
   remain active in some runs. Re-profile categories if a slow mode returns; do not assume VRAM overflow.
3. Keep async compute Stage 2b postponed. Read ASYNC_COMPUTE_PLAN.md; the fast scene still has substantial
   CPU/GPU hand-off latency. Separate compute queue and pipelined dispatch were already measured as no gain.
4. Preserve working defaults. Any further change stays off by default until a repeatable A/B and pictures.

## Build and deploy (PowerShell, project root)

Use bundled `pwsh`; Windows PowerShell could not load scripts/modules in this tool environment. No
execution-policy setting was changed. BUILD_TESTING is now ON in Build/anyps5 so focused ctest runs exist.
Build only the requested targets rather than the entire test suite.

```powershell
$Root = "$PWD"
$env:PATH = "$Root\Tools\winlibs\mingw64\bin;C:\Program Files\Git\cmd;C:\Program Files\Git\usr\bin;$env:PATH"
cmake --build Build\anyps5 --target libs --parallel
Copy-Item Build\anyps5\core\libs\libs\libSceAgcDriver.prx Build\Run\libs\ -Force
```

Shader recompiler and GPU driver live in libSceAgcDriver.prx. If libc or libkernel changes, copy the required
PRX files too. Installed fallback backups (created this session, no game data):
`work/libSceAgcDriver.pre-codex.prx` (original, reclaim present but off),
`work/libSceAgcDriver.baseline-no-reclaim.prx`, `work/libSceAgcDriver.profile-resources.prx`.
Do not overwrite other work with the snapshot test helpers; they validate expected source hashes first.

Focused tests:

```powershell
cmake --build Build\anyps5 --target agc_driver_recorder_tests --parallel 4
$env:ANYPS5_REQUIRE_VULKAN='1'
$env:ANYPS5_NO_SHADER_CACHE='1'
$env:VK_LAYER_PATH='C:\VulkanSDK\1.4.363.0\Bin'
$env:VK_INSTANCE_LAYERS='VK_LAYER_KHRONOS_validation'
$env:APS5_LINEAR_BLOCK_TRACKING='1'
ctest --test-dir Build\anyps5 -R '^agc_storage_(upload_lifetime|write_back|writeback_blocks)$' -V
```

Run the same pattern on baseline and change. Check raw logs for VUID errors and a missing requested layer;
ctest can report Passed with validation errors. Clear validation variables before performance runs.

## Benchmark and analysis

- `& Tests\gpu_modes.ps1 -Rounds 3` uses launcher-equivalent defaults, GPU profiling and unique flushed
  CSVs. Optional `-Env @{ ... }` overrides, `-CpuSamples @(90,120)` records thread CPU usage.
- `& Tests\ab_bench.ps1 -Test @{ SWITCH='1' } -Rounds 3 -GpuTelemetry -Env @{ SWITCH='0' }`:
  common Env overrides are applied to both modes, then Test overrides enabled mode; even rounds reverse
  order. For linear shadows, explicitly set base to 0 because launcher defaults now enable it.
- `python Tests\presents.py <run folders>` gives established presents/10 s medians (/10 = FPS).
- `python Tests\gpu_report.py <run folders> --smi <CSV paths>` gives statistical medians of the last
  eight timing windows and GPU telemetry after 70 s. Exclude cold-cache/menu transitions manually.
- `python Tools\AnyPS5\tools\trace_digest.py Logs\run-...\aps5-summary.jsonl` provides upstream summaries.
- Always open screenshots; use actual shot filenames (a final capture can be 0151 rather than 0150).
- Harness refuses a running AstroBot before redirecting app0, verifies its expected junction and free
  space, and restores app0 to Game afterward. Original game files are read only; overlay args are separate.
- nvidia-smi's continuous `-l -f` buffered short captures and lost output when stopped. gpu_telemetry.py
  queries and flushes every sample; do not rely on the empty first `smi-modes-20261030-015926-502-*.csv`.
- Resource diagnostics print `layers` as the descriptor's encoded depthOrLastArray, not a derived count.
  Output is bounded: top twelve traffic groups every 10 s, 4096 groups maximum, dropped count reported.

Record each completed experiment at the TOP of OVERNIGHT.md. Next active patch number is 0016. Commit only
the intended source files locally (Conventional Commit), export the engine commit, and commit the patch,
launcher, tests and docs in the main repo. Do not stage all the unrelated pre-existing untracked Logs or
Tools/KytyPS5 files. Full experiment history, including failed attempts, remains in OVERNIGHT.md and Git.
