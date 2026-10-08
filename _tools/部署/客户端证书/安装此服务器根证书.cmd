@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0安装客户端根证书.ps1" -CertificatePath "%~dp0SwiftExport_Caddy_Root.cer" -ExpectedThumbprint B392E3634C26A0BE5EFADD6C1965DF4A0145F7EE
pause
