@echo off
cd /d "%~dp0"
rem PowerShell 7 si esta instalado; si no, Windows PowerShell 5.1 (los scripts son compatibles).
where pwsh.exe >nul 2>nul
if errorlevel 1 (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\iniciar.ps1"
) else (
  pwsh.exe -NoProfile -File "%~dp0scripts\iniciar.ps1"
)
if errorlevel 1 (
  pause
  exit /b 1
)
start "" http://localhost:5173
