@echo off
setlocal
if not defined ASTROHYBRID_FPS_LIMIT set "ASTROHYBRID_FPS_LIMIT=30"
echo In the game window, press Ctrl+F2 to cycle 30, 60, 120 and No limit.
echo Press F2 to show or hide the overlay.
call "%~dp0Run-AstroHybrid-NonRT-Cached.bat" %*
exit /b %ERRORLEVEL%
