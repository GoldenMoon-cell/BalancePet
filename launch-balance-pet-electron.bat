@echo off
cd /d "%~dp0versions\electron"
start "" "%CD%\node_modules\electron\dist\electron.exe" --in-process-gpu .
