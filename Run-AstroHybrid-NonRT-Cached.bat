@echo off
setlocal
set "ASTROHYBRID_CACHE_TITLE_ID=PPSA21567"
call "%~dp0Run-AstroHybrid-NonRT.bat" %*
exit /b %ERRORLEVEL%
