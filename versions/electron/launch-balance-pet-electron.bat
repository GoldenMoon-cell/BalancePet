@echo off
cd /d "%~dp0"
start "" "%CD%\node_modules\electron\dist\electron.exe" --in-process-gpu .
