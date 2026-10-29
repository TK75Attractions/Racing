@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-update.ps1"
if errorlevel 1 pause
