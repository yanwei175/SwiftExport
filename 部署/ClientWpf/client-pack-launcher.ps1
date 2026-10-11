#Requires -Version 5.1
# 为 ASCII CMD 提供稳定入口；中文业务脚本由 PowerShell 使用 Unicode 路径定位。
[CmdletBinding()]
param(
    [string]$Version,
    [string]$UpdateUrl,
    [string]$ApiUrl,
    [string]$Notes,
    [switch]$NonInteractive,
    [switch]$NoOpenFolder
)
$ErrorActionPreference = 'Stop'
# 嵌套脚本的 exit 只结束被调用脚本；显式传播失败码给 CMD 或自动化发布流程。
$global:LASTEXITCODE = 0
& (Join-Path $PSScriptRoot '一键打包客户端.ps1') @PSBoundParameters
exit $LASTEXITCODE
