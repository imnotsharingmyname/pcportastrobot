@echo off
rem AstroHybrid with NVIDIA DLSS Frame Generation (2026-11-12, OVERNIGHT.md). Everything else is Run-AstroHybrid.bat.
rem  - Frame Generation: NVIDIA's AI draws extra frames between the game's own, so the screen shows about
rem    2x (or more) the frames the game renders. Uses the game's real depth, motion vectors and camera.
rem    It only runs while the game window has focus (NVIDIA's rule); it adds a little input delay, which
rem    NVIDIA Reflex (switched on with it) keeps low. Do not also turn on NVIDIA Smooth Motion for this game.
rem  - DLSS Super Resolution replaces the game's own temporal upscale (picture quality).
rem  - ASTROHYBRID_CUDA_HOLE keeps an address window free for NVIDIA's CUDA layer, which both need.
rem Options, set before running this file:
rem   ASTROHYBRID_DLSSG_FRAMES=1..5  generated frames per game frame (default 1 = 2x; 3 = 4x). The runtime on
rem     this PC allows up to 5; values above what it allows are lowered automatically.
rem   ASTROHYBRID_DLSS=0  Frame Generation without DLSS Super Resolution.
rem The log (Logs\hybrid-run.out.log) has "DLSS-G 10s:" lines with the frames shown per game frame.
setlocal
if not defined ASTROHYBRID_CUDA_HOLE set ASTROHYBRID_CUDA_HOLE=1
if not defined ASTROHYBRID_DLSSG set ASTROHYBRID_DLSSG=1
if not defined ASTROHYBRID_DLSSG_FRAMES set ASTROHYBRID_DLSSG_FRAMES=1
if not defined ASTROHYBRID_DLSS set ASTROHYBRID_DLSS=1
if not defined ASTROHYBRID_DLSS_PATH set "ASTROHYBRID_DLSS_PATH=%~dp0Build\RunHybrid"
call "%~dp0Run-AstroHybrid.bat" %*
exit /b %ERRORLEVEL%
