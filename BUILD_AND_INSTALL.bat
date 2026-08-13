@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0BUILD_AND_INSTALL.ps1" %*
if errorlevel 1 pause
endlocal
