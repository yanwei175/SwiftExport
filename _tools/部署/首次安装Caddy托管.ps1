#Requires -Version 5.1
<#! 在测试服务器首次安装 Windows 服务与 Caddy，按需停止指定 IIS 站点；后续更新使用后端部署窗口。 !#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PackagePath,
    [Parameter(Mandatory=$true)][string]$ProductionConfigurationPath,
    [string]$ServerAddress = '192.168.3.12',
    [string]$BackendInstallPath = 'D:\Apps\SwiftExport_Service',
    [string]$CaddyInstallPath = 'D:\Apps\SwiftExport_Caddy',
    [string]$CaddyDataPath = 'D:\ERPDeploy\CaddyData',
    [string]$CertificateExportPath = 'D:\ERPDeploy\客户端证书',
    [ValidateRange(1024,65535)][int]$BackendPort = 5081,
    [string]$IisSiteName = 'SwiftExport_WebApi',
    [switch]$NoIis,
    [switch]$PlanOnly,
    [scriptblock]$Progress = { param($message) Write-Host $message }
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '首次托管核心.ps1')
Invoke-CaddyFirstInstallation @PSBoundParameters
