@echo off
chcp 65001 >nul
rem 双击自动请求管理员权限；授权后打开首次安装窗口。
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0管理员启动窗口.ps1" -WindowScript "Caddy首次安装窗口.ps1"
