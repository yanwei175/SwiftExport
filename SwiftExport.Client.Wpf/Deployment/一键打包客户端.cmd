@echo off
chcp 65001 >nul
rem 启动交互打包工具并保留成功或失败提示。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0一键打包客户端.ps1"
set "packExitCode=%errorlevel%"
pause
exit /b %packExitCode%
