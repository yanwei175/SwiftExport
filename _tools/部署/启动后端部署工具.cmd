@echo off
setlocal
if not exist "%~dp0deployment-launcher.ps1" (
    echo Missing deployment-launcher.ps1. Extract the complete tools ZIP first.
    pause
    exit /b 1
)
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0deployment-launcher.ps1" -Mode Update
if errorlevel 1 pause
endlocal
