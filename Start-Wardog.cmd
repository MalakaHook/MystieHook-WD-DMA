@echo off
setlocal
cd /d "%~dp0"
set "LOGDIR=%LOCALAPPDATA%\MystieHook\Logs"
if not exist "%LOGDIR%" mkdir "%LOGDIR%"
set "COREHOST_TRACE=1"
set "COREHOST_TRACEFILE=%LOGDIR%\host-latest.log"
echo Starting MystieHook. Output is being saved to "%LOGDIR%\console-latest.log".
"%~dp0MystieHook.exe" %* > "%LOGDIR%\console-latest.log" 2>&1
set "RESULT=%ERRORLEVEL%"
type "%LOGDIR%\console-latest.log"
echo.
echo MystieHook exited with code %RESULT%.
echo Logs: %LOGDIR%
if not "%RESULT%"=="0" pause
exit /b %RESULT%
