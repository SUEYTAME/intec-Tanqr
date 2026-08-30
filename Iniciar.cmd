@echo off
cd /d "%~dp0"
pwsh.exe -NoProfile -File "%~dp0scripts\iniciar.ps1"
if errorlevel 1 (
  pause
  exit /b 1
)
start "" http://localhost:5173
