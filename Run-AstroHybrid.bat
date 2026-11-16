@echo off
rem AstroHybrid: AstroBot.exe (AnyPS5 native port) rendering with Kyty's GPU stack (kyty_gpu.dll).
rem Engine selection: this launcher uses Build\RunHybrid; Run-AstroBot.bat keeps the AnyPS5 renderer
rem in Build\Run, which is the reliable fallback. Rebuild RunHybrid with
rem   python -I Tools\hybrid\make_run_hybrid.py
setlocal
set "ROOT=%~dp0"
if not exist "%ROOT%Build\RunHybrid\AstroBot.exe" (
  echo Build\RunHybrid not found. Run: python -I Tools\hybrid\make_run_hybrid.py
  exit /b 1
)
set APS5_GPU_ENGINE=kyty
rem Keep commit-epoch fastpath off and buffer-range fixes on so cloth, blankets, and bubbles
rem do not corrupt into stray polygons (issues #30, #35).
set "KYTY_BARRIER_BATCH="
set "KYTY_DISPATCH_OVERLAP="
set "KYTY_WAVE64_WARPS="
set "ASTROHYBRID_COMMIT_EPOCH_FASTPATH=0"
set "KYTY_BUFFER_RANGE_FIXES=1"
rem Hang watchdog (A/B 2026-11-07, OVERNIGHT.md): the preset's KYTY_HANG_WATCHDOG=auto turns it on for
rem RTX 50 GPUs, where it costs the command processor ~1.8 ms per frame in Sky Garden (CP 33.7 -> 31.9 ms).
rem It only records diagnostics when the game hangs. ASTROHYBRID_HANG_WATCHDOG=1 (or KYTY_HANG_WATCHDOG
rem set by you) brings it back; set it when reporting a freeze.
if not defined KYTY_HANG_WATCHDOG if not "%ASTROHYBRID_HANG_WATCHDOG%"=="1" set KYTY_HANG_WATCHDOG=0
rem Persistent shader and pipeline cache behaviour (verified 2026-11-08; see OVERNIGHT.md): without the title ID the
rem hybrid never opened Kyty's caches and compiled shaders during play (100-117 ms hitches); with it, no
rem late compiles and no frame over 80 ms. ASTROHYBRID_SHADER_CACHE=0 turns it off.
if not defined ASTROHYBRID_CACHE_TITLE_ID if not "%ASTROHYBRID_SHADER_CACHE%"=="0" set ASTROHYBRID_CACHE_TITLE_ID=PPSA21567
if exist "%ROOT%Build\RunHybrid\u59-preset.cmd" call "%ROOT%Build\RunHybrid\u59-preset.cmd"
rem Prefer the soft-assert NonRT build for the command-buffer overflow path (issue #25).
rem Callers can still override ASTROHYBRID_EXE explicitly; older runtimes fall back safely.
if not defined ASTROHYBRID_EXE if exist "%ROOT%Build\RunHybrid\AstroBot-NonRT-SoftAssert.exe" set ASTROHYBRID_EXE=AstroBot-NonRT-SoftAssert.exe
if not defined ASTROHYBRID_EXE if exist "%ROOT%Build\RunHybrid\AstroBot-NonRT.exe" set ASTROHYBRID_EXE=AstroBot-NonRT.exe
if not defined ASTROHYBRID_EXE set ASTROHYBRID_EXE=AstroBot.exe
set KYTY_GPU_HOST_MODULE=astrohybrid_host.dll
rem scePadGetTriggerEffectState answers from the game's trigger effect and the trigger travel, as a
rem DualSense does; Astro Bot ignores L2/R2 while it reads "no effect" (2026-11-07). Rumble, light bar and
rem trigger forces reach the controller through Kyty (ABI 1.4). APS5_PAD_TRIGGER_STATE=0 restores zeros.
if not defined APS5_PAD_TRIGGER_STATE set APS5_PAD_TRIGGER_STATE=1
rem Older Astro Bot builds (e.g. PPSA21564 01.007.000, issue #27) call sceSaveDataTransferringMount
rem as their initial save mount; opt into the mount3 fallback unless explicitly disabled.
if not defined APS5_SAVEDATA_TRANSFER_FALLBACK set APS5_SAVEDATA_TRANSFER_FALLBACK=1
rem Motion sensors (2026-11-12, OVERNIGHT.md): the controller's accelerometer and gyroscope reach the game
rem (shake and tilt moves); before, the game always read a controller at rest. ASTROHYBRID_PAD_MOTION=0 restores that.
if not defined ASTROHYBRID_PAD_MOTION set ASTROHYBRID_PAD_MOTION=1
rem Vibration strength of pads other than a DualSense (Xbox, Switch, generic), as percent of what the game
rem asks for: ASTROHYBRID_RUMBLE_SCALE=0..200 (default 100). ASTROHYBRID_RUMBLE_SWITCH_FLOOR=n (default 0)
rem lifts a Switch-type pad's motor to at least n of 255; the launchers pass 96 scaled by the strength, so a
rem light setting stays light on Joy-Cons and Switch Pro pads (issue #36).
rem Priority (A/B 2026-11-07, OVERNIGHT.md): the command processor at THREAD_PRIORITY_HIGHEST and the
rem process above normal keep other programs off the reserved CP core. ASTROHYBRID_PRIORITY=normal and
rem KYTY_CP_PRIORITY=1 restore the previous behaviour.
if not defined KYTY_CP_PRIORITY set KYTY_CP_PRIORITY=2
rem Draw-prep helper priority (A/B 2026-11-07 22:33-22:43, OVERNIGHT.md): with other programs running, a
rem helper that holds the CP's next draw was pre-empted and the CP spun 3-4 ms per frame; helpers at the
rem CP's priority (2) with short spins cut that to 0.4-0.7 ms and CP time by ~1.2 ms per frame.
rem ASTROHYBRID_DRAW_PREP_TUNING=0 restores the previous helper behaviour.
if not "%ASTROHYBRID_DRAW_PREP_TUNING%"=="0" (
  if not defined KYTY_DRAW_PREP_PRIORITY set KYTY_DRAW_PREP_PRIORITY=2
  if not defined KYTY_DRAW_PREP_SPIN_US set KYTY_DRAW_PREP_SPIN_US=10
  if not defined KYTY_DRAW_PREP_COLD_SPIN_US set KYTY_DRAW_PREP_COLD_SPIN_US=5
)
if not defined ASTROHYBRID_PRIORITY set ASTROHYBRID_PRIORITY=abovenormal
if not defined KYTY_GPU_WIDTH set KYTY_GPU_WIDTH=1920
if not defined KYTY_GPU_HEIGHT set KYTY_GPU_HEIGHT=1080
if not exist "%ROOT%Logs" mkdir "%ROOT%Logs"
set "ASTROHYBRID_EXTRA_ARGS="
if "%ASTROHYBRID_SKIP_INTRO%"=="1" set "ASTROHYBRID_EXTRA_ARGS=-skipIntro"
cd /d "%ROOT%Build\RunHybrid"
echo Logging to Logs\hybrid-run.out.log / Logs\hybrid-run.err.log
start "" /b /wait /%ASTROHYBRID_PRIORITY% "%ROOT%Build\RunHybrid\%ASTROHYBRID_EXE%" %ASTROHYBRID_EXTRA_ARGS% %* 1>"%ROOT%Logs\hybrid-run.out.log" 2>"%ROOT%Logs\hybrid-run.err.log"
set CODE=%ERRORLEVEL%
echo AstroBot.exe exited with %CODE%
exit /b %CODE%
