@echo off
cd /d "%~dp0"
echo This is the Python fallback. For the normal app, use launch-balance-pet.bat.
start "BalancePet Python fallback" pythonw balance_pet.py
