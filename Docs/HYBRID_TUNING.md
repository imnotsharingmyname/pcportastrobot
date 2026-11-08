# AstroHybrid tuning — November 7, 2026

Target: 40 FPS in Sky Garden, then 60. **40 FPS has not been achieved.**
All changes remain opt-in. Do not infer gains from different scenes or isolated FPS readings.

## Kept: short-spin, raised-priority preparation workers

Kyty commit `1d12195c`, exported in
`Source/hybrid/patches/0001-draw-prep-worker-priority.patch`.

Native switch `KYTY_DRAW_PREP_PRIORITY`: absent/0 leaves worker priority unchanged;
1 selects AboveNormal thread priority, 2 Highest. This affects only the calling workers.
The process still uses the established AboveNormal priority and `KYTY_CP_PRIORITY=2`.

Measured useful combination:

```text
KYTY_DRAW_PREP_PRIORITY=1
KYTY_DRAW_PREP_SPIN_US=10
KYTY_DRAW_PREP_COLD_SPIN_US=5
```

Keep six workers and two hot workers. Raising worker priority alone mostly moves deep
preparation wait into CP idle wait: high-priority workers also spin while guest threads need CPUs.
Shortening the spin alone did not help.

The launcher enables this combination only with
`ASTROHYBRID_DRAW_PREP_TUNING=1`. Absent/0 preserves previous behavior; explicit underlying
environment variables override the profile. Four isolated launcher smoke tests verify these
branches and the established AboveNormal process class without loading the game.

Two control/candidate/control triplets, 150 seconds each, PGO, same idle scene:

| Runs (control / candidate / control) | CSV FPS | Candidate p99 / 1% low |
|---|---|---|
| 041858 / 042131 / 042404 | 30.180 / 32.727 / 31.256 | 42.049 ms / 22.561 FPS |
| 045624 / 045857 / 050130 | 29.541 / 30.464 / 30.347 | 45.427 ms / 21.271 FPS |

Combined mean FPS improves 4.17% against the mean of each pair's flanking controls;
slowest-1%-average FPS improves 12.37%. CP gap improves approximately 1 ms/frame.
Both enabled final images were inspected and show the expected scene. This is not a
claim of sustained 32.7 FPS in every run.

## Rejected or not established

- Process EcoQoS override: no repeatable throughput gain; original AnyPS5 source restored,
  libkernel rebuilt and deployed. Patch retained in `Source/experiments`.
- Hot=4: no repeatable advantage over flanking controls; previous unpaired default promotion
  was withdrawn. The historical control exited after five seconds.
- Two workers: substantially more deep wait, also with priority and short spins.
- Four workers, all hot: no material throughput gain, worse 1% low.
- Disabling the reserved CP core: ~26.7 versus ~31.2 FPS; per-draw CP cost rises.
- Larger binding memos: 18-bit table slower; 16-bit table ties controls.
  Experimental constructor change archived and restored, not kept in the engine.
- Highest worker priority with short spins: not clearly preferable to AboveNormal.

## Structural evidence and next experiments

The profile's `instance` bucket means a rendering-instance/command-buffer change, **not**
object instancing (`commitStats.cpp::EndDraw`). Do not optimize `instance_count` based on it.

Diagnostic GPU capture `Logs/gpuends-20261107-041858`, flip 2731:
4804 draws, 278 dispatches, 124 command buffers, 255 render-pass starts. Of 99 untagged
rendering ends, 80 are last in their command buffer and 15 precede only final barrier/segment
work. Another 92 ends are target/state changes. This suggests testing optional submission
batching before attempting unsafe cross-thread cache access or command-buffer splitting.
Timestamp captures perturb timing and are not A/B throughput measurements.

Existing runtime switches to test next, each against alternating controls:
`KYTY_SUBMIT_MIN_INTERVAL_US`, `KYTY_IDLE_FLUSH_DRAWS`, `KYTY_GFX_SLICE_DRAWS`.

A separate CPU-ISA build experiment is prepared in `Tools/hybrid/build_gpu_v3.cmd`.
It requires explicit `ASTROHYBRID_BUILD_V3=1`, checks for a running game and disk headroom,
uses existing offline dependencies and PGO, and writes a separate E: build/DLL.
It does not deploy or change the default DLL. It has not yet established a performance gain.

## Measurement and safety

`Tests/hybrid_cp_batch.ps1` runs alternating named cases, sets only the game process AboveNormal,
waits for process exit, checks C: headroom and writes each result at the top of `OVERNIGHT.md`.
`-PrepTune` explicitly enables the kept scheduling combination for every case in a batch.

`Tests/hybrid_cp_report.py` reads UTF-8/UTF-16 logs, the last six CP windows and the headerless
microsecond frame CSV. Frame statistics use its last 60 seconds. The 1% low is reciprocal of
the average slowest 1% frame time, not reciprocal p99. Raw results and CSV paths are retained.

The first new runs 033431/033704/033937 were contaminated by an inherited recursive PowerShell
search from the previous takeover; the agent-owned search was identified by its command line
and stopped. These runs cannot support a gain claim.

Game files and the archive remain untouched. No pushes, driver installs, registry edits or
global power changes. Keep C: above 30 GiB and do not build during performance runs.
