@echo off
rem AstroHybrid with the non-RT lighting patch (AstroBot-NonRT.exe): the version every 2026-11-07
rem benchmark in Docs\OVERNIGHT.md used. Same settings and defaults as Run-AstroHybrid.bat.
setlocal
set ASTROHYBRID_EXE=AstroBot-NonRT.exe
call "%~dp0Run-AstroHybrid.bat" %*
exit /b %ERRORLEVEL%
