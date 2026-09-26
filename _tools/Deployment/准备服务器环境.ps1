#Requires -Version 5.1
#Requires -RunAsAdministrator
<#! 仅在首次部署的 Windows Server 执行；Hosting Bundle 使用管理员从微软取得的离线安装文件。 !#>
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$HostingBundleInstaller)
$ErrorActionPreference = 'Stop'
$installer = (Resolve-Path -LiteralPath $HostingBundleInstaller).Path
$signature = Get-AuthenticodeSignature -LiteralPath $installer
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw '安装程序必须具有有效的微软签名，请从微软下载 .NET 10 Hosting Bundle。' }
Import-Module ServerManager
$result = Install-WindowsFeature Web-Server,Web-Mgmt-Console,Web-Scripting-Tools
if (!$result.Success) { throw 'IIS 组件安装失败。' }
if ($result.RestartNeeded -eq 'Yes') { throw 'Windows 功能安装要求重启。请重启后重新运行本脚本，不自动重启服务器。' }
# 安装不显示额外窗口；若要求重启，只报告，不自动中断数据库服务。
$process = Start-Process -FilePath $installer -ArgumentList '/install','/quiet','/norestart' -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -notin @(0,3010)) { throw "Hosting Bundle 安装失败，退出码 $($process.ExitCode)。" }
Write-Output '安装步骤已完成。请在首次部署维护时间重启服务器，使 IIS 加载托管模块；脚本未自动重启。'
