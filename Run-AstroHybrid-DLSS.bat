@echo off
rem AstroHybrid with NVIDIA features on (2026-11-12, OVERNIGHT.md). Everything else is Run-AstroHybrid.bat.
rem  - DLSS Super Resolution replaces the game's own temporal upscale (1920x1080 -> 2432x1368 in Sky Garden),
rem    using the game's colour, depth, motion vectors and jitter. Picture quality, not FPS: the frame rate is
rem    limited by the CPU side of the emulator. Needs an RTX GPU; without DLSS the game's pass runs as before.
rem  - ASTROHYBRID_CUDA_HOLE keeps 0xDC00000000-0xEC00000000 out of the PS5 memory map so NVIDIA's CUDA layer,
rem    which DLSS needs, can start in this process.
rem  - Low latency (VK_NV_low_latency2, the Vulkan side of Reflex) paces the game's frames to cut queued frames.
rem Options, set before running this file:
rem   ASTROHYBRID_DLSS_QUALITY=quality^|balanced^|performance^|dlaa  DLSS model family (default quality). The
rem     input and output sizes are the game's own, so this does not change the rendering resolution.
rem   ASTROHYBRID_LOW_LATENCY=0  turns low latency off.
rem The console prints "DLSS 10s:" and "LowLatency 10s:" lines that show both are actually running.
setlocal
if not defined ASTROHYBRID_CUDA_HOLE set ASTROHYBRID_CUDA_HOLE=1
if not defined ASTROHYBRID_DLSS set ASTROHYBRID_DLSS=1
if not defined ASTROHYBRID_DLSS_PATH set "ASTROHYBRID_DLSS_PATH=%~dp0Build\RunHybrid"
if not defined ASTROHYBRID_LOW_LATENCY set ASTROHYBRID_LOW_LATENCY=1
call "%~dp0Run-AstroHybrid.bat" %*
exit /b %ERRORLEVEL%
