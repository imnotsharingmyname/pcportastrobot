@echo off
rem Separate x86-64-v3 (AVX2) experiment of kyty_gpu.dll, deployed to Build\RunHybrid with a backup of the
rem previous DLL. The build itself is build-kyty.ps1 (Build\kyty-pgo-v3, DLL in Build\kyty-pgo-v3-gpu).
setlocal
if not "%ASTROHYBRID_BUILD_V3%"=="1" (
  echo Opt-in only: set ASTROHYBRID_BUILD_V3=1 to build the separate x86-64-v3 experiment.
  exit /b 2
)
powershell -NoProfile -Command "if (Get-Process AstroBot* -ErrorAction SilentlyContinue) { exit 1 }"
if errorlevel 1 (
  echo Close the game first: the build replaces the DLL it has loaded.
  exit /b 3
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-kyty.ps1" -March x86-64-v3 -Name kyty-pgo-v3 -Deploy
exit /b %ERRORLEVEL%
