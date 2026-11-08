@echo off
rem Astro Bot launcher: opens the settings window (lighting, frame rate, window size, fullscreen),
rem then starts AstroHybrid (Run-AstroHybrid.bat) with those settings.
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0Tools\hybrid\launcher.ps1"
exit /b %ERRORLEVEL%
