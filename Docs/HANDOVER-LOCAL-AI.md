# Handover for the next AI: Astro Bot PC port (written 2026-11-08 by Claude)

Read this whole file before doing anything. It is written step by step on purpose. When something here says
"measure", it means: run the benchmark, read the numbers, and write them down. Never guess a result.

---------------------------------------------------------------------------------------------------------------

## 0. Who you are working for and the hard rules

The user owns a PS5 copy of Astro Bot (PPSA21567, version 01.019.000) and wants it to run well on their Windows PC:
Ryzen 5 5500 (6 cores / 12 threads), RTX 5060 8 GB, 2560x1440 164 Hz monitor, DualSense over Bluetooth.
The user is not a programmer. Explain things in plain words, keep answers short, and work on your own without asking
for permission at every step. They prefer quick, short tests (about 100-120 seconds) over long benchmark sessions.

Project folder: `C:\Users\GAMING PC\Desktop\AstroBot-PC-Port`. Read `AGENTS.md` there. The rules that matter most:

1. NEVER modify, move or delete anything in `Game\` (the game files) or `E:\PPSA21567.7z`. Read only.
2. NEVER download Sony SDKs, keys, firmware or game files. Ask the user before downloading anything at all.
3. Keep at least 30 GB free on drive C:. Only delete files you created yourself.
4. No system changes: no drivers, registry, antivirus, Windows settings.
5. Every new feature goes behind an environment variable that is OFF by default. Only make it default after a test
   shows it works.
6. After every test, add one row at the TOP of the table in `Docs\OVERNIGHT.md` with the result, good OR bad.
7. Never say something is faster or fixed unless you measured it.
8. Commit with git locally. The public GitHub copy is a separate folder (see section 7); never put game files there.

---------------------------------------------------------------------------------------------------------------

## 1. What this program is (read this so you understand the code)

It is NOT a normal emulator. It is a "native port with a GPU translation layer":

- The PS5 has an x86-64 CPU, like the PC. A tool called **AnyPS5** takes the game's own executable code and relinks
  it into a Windows program (`AstroBot.exe`). The CPU runs the game's code directly. Game code is loaded at address
  0x400000, so **game address (eboot VA) = exe address - 0x400000**.
- The PS5 system libraries (files, threads, memory, controller, audio, saves) are rewritten as Windows code in AnyPS5
  (`Tools\AnyPS5\core\libs\prx\...`, each library is a `.prx` file).
- The graphics are the hard part. The game still produces PS5 GPU command buffers and PS5 shaders. A DLL called
  `kyty_gpu.dll`, built from the **KytyPS5** emulator source, reads those commands (the "command processor", CP),
  translates shaders, and draws with Vulkan. This setup is called **AstroHybrid**.

Where things live:

| What | Where |
|---|---|
| Kyty (GPU DLL) source, git repo | `E:\AstroHybrid-Work\source\kyty` (same folder is also reachable as `C:\kyty-src19`) |
| Kyty source files you will touch most | `src\graphics\presentation\videoOut.cpp` (frame limit, game speed), `performanceOverlay.cpp`, `window\window.cpp`, `src\graphics\host_gpu\...` (renderer), `src\hybrid\kytyGpuBridge.cpp` |
| AnyPS5 source | `Tools\AnyPS5` (its own rules: `Tools\AnyPS5\AGENTS.md`) |
| Our patch copies (for GitHub) | `Source\patches` (AnyPS5), `Source\hybrid\patches` (Kyty, 0001-0011) |
| Ready-to-run game folder | `Build\RunHybrid` (exe, prx libraries, kyty_gpu.dll) |
| Launcher (what the user clicks) | `AstroBot Launcher.exe` in the project root; source `Tools\hybrid\launcher\AstroBotLauncher.cs` |
| Script that sets all tuned defaults | `Run-AstroHybrid.bat` (the launcher calls it) |
| Results log | `Docs\OVERNIGHT.md` (newest row on top) |
| Test logs | `Logs\run-YYYYMMDD-HHMMSS\` (stdout.log, result.txt, screenshot) |
| Logs of the user's own play session | `Logs\hybrid-run.out.log` and `Logs\hybrid-run.err.log` (overwritten each launch) |

---------------------------------------------------------------------------------------------------------------

## 2. How to build, deploy and test (copy these exactly)

Build the Kyty DLL after changing Kyty source (takes 1-5 minutes):

```
cmd /c "E:\AstroHybrid-Work\build\build-kyty-pgo.cmd build"
python -I C:/kyty-src19/tools/hybrid/build_kyty_gpu.py E:/AstroHybrid-Work/build/kyty-pgo E:/AstroHybrid-Work/build/kyty-gpu-<short-name>
```

Then deploy it into the game folder (the game must be CLOSED, check with `tasklist | findstr AstroBot`):

```
set ASTROHYBRID_KYTY_GPU=E:/AstroHybrid-Work/build/kyty-gpu-<short-name>
python -I Tools/hybrid/make_run_hybrid.py
```

The DLL currently in use is `E:\AstroHybrid-Work\build\kyty-gpu-pgo-overlay2`. If your change breaks something,
deploy that one again to go back.

AnyPS5 libraries: `set PATH=Tools\winlibs\mingw64\bin;%PATH%` then `cmake --build Build/anyps5 --target libs`.
Careful: files in `Build\Run\libs` are hard links to `Build\anyps5` outputs, so rebuilding changes them immediately.

Rebuild the launcher exe (the launcher must be closed, otherwise the build fails without a clear message):
`powershell -NoProfile -ExecutionPolicy Bypass -File Tools\hybrid\launcher\build_launcher.ps1`

Test (in PowerShell, from the project folder). This starts the game, loads the user's save into Sky Garden, records
frame times and takes a screenshot at the end:

```
.\Tests\hybrid_skygarden.ps1 -Seconds 110 -Shots 1 -Env @{ ASTROHYBRID_FPS_LIMIT='unlimited'; ASTROHYBRID_CACHE_TITLE_ID='PPSA21567'; ASTROHYBRID_GAME_SPEED='1' }
```

- Look at the screenshot (`Logs\run-...\shot_*.png`) every time: the picture must look right.
- Check `result.txt` and search `stdout.log` for `ErrorDeviceLost` or `FATAL`.
- For an A/B test run: A, B, B, A (same session, same settings except the one you test). Other programs on the PC
  change results, so never compare with runs from another day.
- Before starting a new run, wait until no `AstroBot*` process is left.
- If a test is interrupted and `Build\Run\app0` is broken, fix it with:
  `cmd /c rmdir "Build\Run\app0"` then `cmd /c mklink /J "Build\Run\app0" "Game\PPSA21567-app"`.

Useful environment switches (all read by Kyty or AnyPS5):

| Variable | Meaning |
|---|---|
| `ASTROHYBRID_EXE` | `AstroBot-NonRT.exe` (faster lighting, launcher default) or `AstroBot.exe` (original lighting) |
| `ASTROHYBRID_FPS_LIMIT` | 30, 35, 40, 45, 50, 55, 60, 90, 120, unlimited |
| `ASTROHYBRID_GAME_SPEED=1` | fixes slow motion below 60 FPS (see section 3) |
| `ASTROHYBRID_GAME_SPEED_PERCENT` | 25-150, the user likes 75 |
| `ASTROHYBRID_OVERLAY` | fps, compact, full (stats on screen from the start; F2 / Shift+F2 in game) |
| `ASTROHYBRID_WINDOW_TITLE` | fixed window title |
| `KYTY_GPU_WIDTH` / `KYTY_GPU_HEIGHT` / `KYTY_GPU_FULLSCREEN` | window size, fullscreen |
| `KYTY_DEVICE_FAULT_DIAGNOSTICS=1` | extra information when the GPU crashes (slower) |

---------------------------------------------------------------------------------------------------------------

## 3. What already works (do not break these)

- Game runs from title screen to gameplay; Sky Garden about 30-32 FPS, lighter areas 45+.
- DualSense: buttons, L2/R2, rumble, light bar, adaptive triggers (user confirmed).
- Shader and pipeline cache on disk (`ASTROHYBRID_CACHE_TITLE_ID=PPSA21567`): no shader stutter after first visit.
- Frame limiter with even pacing.
- Slow motion fix: the game moves time forward by 1/FrameRate each frame, read from one global block at exe address
  0x93F0040 (double frame rate, float 1/rate, uint64 microseconds per frame). The game was made for 60 FPS, so at
  30 FPS it ran at half speed. Kyty function `UpdateGameStep` in `videoOut.cpp` writes the real (smoothed) frame rate
  there each frame. Measured: 0.50x real time before, 0.98x after.
- Launcher with lighting, frame rate, game speed, stats overlay, window size, fullscreen.

---------------------------------------------------------------------------------------------------------------

## 4. The three problems the user wants fixed

### Problem A: the game crashes in some levels (not Sky Garden; a forest/jungle-like level)

Evidence from the user's last session, saved in `Logs\crash-forest-20261108\` (hybrid-run.err.log line ~184):

```
FATAL: unhandled exception 0xc0000005 on thread 'Draw Shadow'
rip AstroBot-NonRT.exe+0x4b0c3e   (game address 0x4a0c3e), read of 0xffffffffffffffff, rcx = deadbeefcafebabe
[rsi] "There are too many objects/batches in this scene. ...gpu\command\_IMPL\_PPR\command_buffer_writer.ppr.cpp:31"
last level files opened: /app0/data/prein/levels/spring_punch_sky_aztec/...
```

What this means in simple words: this is not a random crash. It is the GAME's own safety check. The game's shadow
drawing thread filled up its GPU command buffer ("too many objects/batches"), and the game then deliberately crashed
(the deadbeef value and the read of address -1 are its way of stopping). So in that scene the game tries to draw
more shadow objects than its buffer allows. That never happens on a PS5, so something in our setup makes the game
draw more than on PS5. Investigate in this order, one test each:

1. Lighting patch: the crash happened with `AstroBot-NonRT.exe` (non-RT lighting patch from the community). That
   patch switches the game to a different lighting path, which can change shadow work. Play the same level with
   `ASTROHYBRID_EXE=AstroBot.exe`. If it does not crash, the patch is the cause: tell the user, and make the launcher
   warn about it or use the original lighting for that level.
2. Game speed fix: with `ASTROHYBRID_GAME_SPEED=1` we change the game's frame rate value. Level-of-detail or culling
   code could use it. Retest the level with `ASTROHYBRID_GAME_SPEED=0`.
3. Other game patches in `Source\game-patches` (fixed_1080p, gi_probes_off, non_tiled_lighting), if any are on.
4. If none of these, find where the buffer size is set: disassemble the game around 0x4a0c3e (tools:
   `Tools\winlibs\mingw64\bin\objdump.exe -D -b binary -m i386:x86-64 -M intel --start-address=<file offset> ...
   Analysis\elf\eboot.elf`; file offset = game address + 0x4000 for code). Look for the compare that leads to the
   "too many objects" message and the size it checks. A size that comes from a system call (memory size, GPU
   buffer allocation) can be wrong in our AnyPS5 libraries; a constant can be raised with a game patch
   (format in `Source\game-patches\*.toml`, always keep it optional).

There were also "GPU device lost" crashes on 2026-11-08 (`ErrorDeviceLost` in stdout.log, Windows event nvlddmkm
153). They happened at start-up, also without the shader cache, and later runs were fine. If they come back, run with
`KYTY_DEVICE_FAULT_DIAGNOSTICS=1` and collect the log.

To reach the crashing level the user plays it themselves. Ask them to launch with the launcher, play until it
crashes, and then read `Logs\hybrid-run.err.log` (copy it to a new folder first, the next launch overwrites it).

### Problem B: FPS has to go up a lot

Where the time goes (measured): one frame is about 31-33 ms in Sky Garden. Kyty's command processor (CP), the thread
that reads the game's GPU commands, is the bottleneck: about 22 ms is "draw commit" for about 4,800 draws (about
4.4 microseconds each) plus about 9 ms of waiting/gaps. The GPU itself is only about 68% busy. So the FPS limit is on
the CPU side, inside Kyty, not the graphics card.

Things already tried and REJECTED (do not repeat; details in `Docs\OVERNIGHT.md`): splitting one batch into several
Vulkan command buffers (crashes), dispatches as committer items, deferred copy-backs, 32 MiB shadow window, batch caps,
a second compute queue, occlusion queries, work stealing, placing threads on SMT siblings, bigger memo sizes, several
CP tuning switches.

Promising leads, best first:
1. Page-fault cost. The log line `Kyty fault cost: last 10 s, 30.0 fps: 102 faults/frame at 207.7 us in the
   handler (21.16 ms/frame)` showed 10-30 ms per frame spent in the memory write-fault handler (Kyty write-protects
   GPU memory pages to notice when the game changes textures/buffers). Find out whether that time is on the CP's
   critical path (which threads take the faults). If yes, reducing faults (bigger or smarter protection ranges,
   not protecting pages the game rewrites every frame, tracking dirty pages without faults) could give a lot.
2. Cheaper per-draw work in the CP: profile one draw commit (hashing pipeline state, descriptor/binding updates,
   checking memory ranges) and remove repeated work for draws whose state did not change.
3. Draws the game sends that are not visible or do nothing.

Always measure each idea alone with an A/B run. Write every result in OVERNIGHT.md.

### Problem C: graphical glitches in some places

The user saw glitches once with the first speed-fix version (the time step jumped every frame; that is smoothed now).
Other glitches are not described yet. Ask the user for a screenshot and the place. Known suspects: the non-RT
lighting patch (compare with `AstroBot.exe`), MRT split messages in the log ("MRT split: draw #N runs a second pass"),
and shader translation errors (search stdout.log for "error", "unsupported", "fallback").

---------------------------------------------------------------------------------------------------------------

## 5. New feature: upscaling

Be honest with the user about this: upscaling helps when the GRAPHICS CARD is the limit. Right now the CPU side (CP)
is the limit, so upscaling alone will not raise FPS much in Sky Garden. It helps in other ways: a sharper picture on
the 2560x1440 monitor, and FPS headroom later once the CP is faster.

Simple plan:
1. The game picks its own render resolution with a dynamic-resolution controller (levels: 1920x1080, 2432x1368,
   3328x1872, 3840x2160; table at game address 0x8e500a0; patch `Source\game-patches\fixed_1080p.toml` keeps it at
   1080p). Kyty then stretches that picture to the window size when it presents.
2. Add a better scaling filter at that last stretch step: **AMD FSR 1** (EASU upscale + RCAS sharpen). It is a pair of
   small shaders under the MIT licence. Ask the user before downloading the FidelityFX source. Implement it as a
   Vulkan compute pass in Kyty's presenter (`src\graphics\presentation\...`, where the game image is copied to the
   swapchain), behind a switch such as `ASTROHYBRID_UPSCALE=fsr1` with `ASTROHYBRID_SHARPNESS=0.2`.
3. Optionally let the user lower the game's own render resolution (for example 1600x900 or 1280x720 via a patch of
   the resolution table, as an optional .toml game patch) and let FSR bring it back up to the window size. Measure
   whether GPU time goes down.
4. Add the choice to the launcher (`AstroBotLauncher.cs`): "Upscaling: Off / FSR 1 quality / FSR 1 performance".
5. Test with screenshots side by side; check the overlay and the UI text still look right.

---------------------------------------------------------------------------------------------------------------

## 6. How to report to the user

- Short and in plain words: what you changed, what you measured (numbers), what is not done yet.
- If you are not sure something works, say so.
- If you think a decision in this file is wrong, say why, with evidence, before changing it.

## 7. GitHub

The user's public repo: https://github.com/imnotsharingmyname/pcportastrobot. It is a CLEAN copy in
`C:\Users\GAMING PC\Desktop\AstroBot-PC-Port-GitHub` (patches, launcher source, tools, tests, docs; NO game files,
NO exes, NO screenshots, NO logs). To update it: copy changed files there, `git add -A`, commit, `git push`.
Only push when the user asks for it.
