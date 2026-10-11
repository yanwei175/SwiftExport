@echo off
rem Run the local packaging tool and retain its exit code.
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0client-pack-launcher.ps1"
set "packExitCode=%errorlevel%"
pause
exit /b %packExitCode%
