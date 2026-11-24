@echo off
rem PGO + ThinLTO build of kyty_gpu.dll (Jetsku's int21 recipe). Kept for old instructions: the build now lives in
rem build-kyty.ps1, which finds Visual Studio, LLVM 22.1.3, the Vulkan SDK and the DLSS/FSR SDKs on this PC.
rem Usage: build-kyty-pgo.cmd [configure|build]   (configure = configure and build; build = skip configure)
setlocal
set "ARGS="
if "%1"=="build" set "ARGS=-SkipConfigure"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-kyty.ps1" %ARGS%
exit /b %ERRORLEVEL%
