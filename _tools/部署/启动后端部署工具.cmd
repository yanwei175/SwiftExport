@echo off
chcp 65001 >nul
rem 双击自动请求管理员权限；授权后打开日常更新窗口。
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0管理员启动窗口.ps1" -WindowScript "后端部署窗口.ps1"
