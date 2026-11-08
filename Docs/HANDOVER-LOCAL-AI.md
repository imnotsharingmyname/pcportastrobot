# Handover prompt for the next AI (2026-11-08)

You are continuing a native Windows port of my own PS5 copy of Astro Bot (PPSA21567 v01.019.000) in
`C:\Users\GAMING PC\Desktop\AstroBot-PC-Port`. Read `AGENTS.md` first and obey its hard rules: never touch
`Game\` or `E:\PPSA21567.7z`, never download Sony material, keep 30 GB free on C:, no system changes, no git push,
new behaviour behind an env switch that is off by default, log every measured result (good or bad) at the top of
`Docs\OVERNIGHT.md`. Never claim a speed-up or fix you did not measure.

## Architecture ("AstroHybrid")
- AnyPS5 relinks the game's x86-64 code into `AstroBot.exe` (game code at 0x400000 = eboot VA + 0x400000) and
  reimplements PS5 system libraries as native `.prx` modules. Source: `Tools\AnyPS5` (patches in `Source\patches`).
- The GPU side is KytyPS5's stack built as `kyty_gpu.dll`. Source: `E:\AstroHybrid-Work\source\kyty`
  (junction `C:\kyty-src19`), patches mirrored in `Source\hybrid\patches`.
- Build the DLL: `E:\AstroHybrid-Work\build\build-kyty-pgo.cmd build`, then
  `python -I C:/kyty-src19/tools/hybrid/build_kyty_gpu.py E:/AstroHybrid-Work/build/kyty-pgo E:/AstroHybrid-Work/build/<new-dir>`,
  deploy with `ASTROHYBRID_KYTY_GPU=<dir> python -I Tools/hybrid/make_run_hybrid.py` (fills `Build\RunHybrid`).
- Launch: `AstroBot Launcher.exe` (root; source `Tools\hybrid\launcher`, rebuild with `build_launcher.ps1`).
  It calls `Run-AstroHybrid.bat`, which holds all tuned defaults. Old launchers are in `Old launchers\`.
- Test: `Tests\hybrid_skygarden.ps1 -Seconds 120 -Env @{...}` (Sky Garden benchmark, screenshot, logs in `Logs\run-*`).
  Keep runs short (100-120 s), flank A/B with same-session controls, wait until no `AstroBot*` process remains.

## State now
- Sky Garden ~30-32 FPS, lighter areas 45+. The limit is Kyty's command processor (CP): ~22 ms of draw commit
  (~4,800 draws x 4.4 us) + ~9 ms gap per frame; GPU ~68% busy.
- NEW: slow motion below 60 FPS is fixed. The game steps 1/FrameRate per frame from one global block (exe
  0x93F0040: f64 rate, f32 1/rate, u64 us/frame; setter eboot 0x34c0). `ASTROHYBRID_GAME_SPEED=1` (Kyty patch
  0009, in `videoOut.cpp` `UpdateGameStep`) writes the measured present rate there. Measured 0.50x -> 0.98x real
  time at 30 FPS. The launcher turns it on. Not yet checked: physics/cutscenes with swinging frame rates.
- DualSense (buttons, L2/R2, rumble, light bar, trigger effects) works, user-confirmed.
- Two GPU device-lost crashes (`ErrorDeviceLost`, Windows event nvlddmkm 153) happened on 2026-11-08 at start-up,
  also without the shader cache; later runs were fine. Watch for it.
- Rejected ideas are listed in `Docs\OVERNIGHT.md`; do not retry them without a new reason.

## Suggested next steps
1. Confirm the real-time speed in more levels (cutscenes, physics-heavy scenes, boss fights); compare against
   60 FPS behaviour. If anything drifts, try snapping the rate to the cap or smoothing more.
2. Reduce CP draw-commit cost (the real FPS limit): cheaper per-draw state hashing/binding in Kyty's renderer.
3. Investigate the device-lost crashes if they come back (`KYTY_DEVICE_FAULT_DIAGNOSTICS=1`).

If you think a decision here is wrong, say so with evidence before changing it.
