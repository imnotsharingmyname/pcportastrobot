@echo off
setlocal
set "ASTROHYBRID_FPS_LIMIT="
for /f "usebackq delims=" %%F in (`powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0..\Tools\hybrid\fps_settings.ps1"`) do (
  if "%%F"=="30" set "ASTROHYBRID_FPS_LIMIT=30"
  if "%%F"=="60" set "ASTROHYBRID_FPS_LIMIT=60"
  if "%%F"=="120" set "ASTROHYBRID_FPS_LIMIT=120"
  if "%%F"=="unlimited" set "ASTROHYBRID_FPS_LIMIT=unlimited"
)
if not defined ASTROHYBRID_FPS_LIMIT exit /b 0
call "%~dp0Run-AstroHybrid-NonRT-Cached.bat" %*
exit /b %ERRORLEVEL%
