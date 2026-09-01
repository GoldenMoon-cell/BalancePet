@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0balancepet-task.ps1" %*
exit /b %ERRORLEVEL%
