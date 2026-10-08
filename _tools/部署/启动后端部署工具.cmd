@echo off
chcp 65001 >nul
rem 双击打开本地部署窗口；执行策略选项仅作用于本次 PowerShell 进程。
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0后端部署窗口.ps1"
