@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run-native.ps1"
if errorlevel 1 (
  echo GGD-help could not start. See the error above.
  pause
  exit /b 1
)
