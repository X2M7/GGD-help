@echo off
cd /d "%~dp0"
if exist "release\GGD-help-V0.1.1-win-x64.exe" (
  start "" "release\GGD-help-V0.1.1-win-x64.exe"
  exit /b
)
if exist "out\GGD-help-win32-x64\GGD-help.exe" (
  start "" "out\GGD-help-win32-x64\GGD-help.exe"
  exit /b
)
if not exist "node_modules\electron\dist\electron.exe" (
  echo Run npm install in this folder first.
  pause
  exit /b 1
)
call npm.cmd start
