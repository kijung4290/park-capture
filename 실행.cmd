@echo off
cd /d "%~dp0"
if not exist "bin\ParkCapture.exe" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if not exist "bin\ParkCapture.exe" exit /b 1
start "" "%~dp0bin\ParkCapture.exe"
