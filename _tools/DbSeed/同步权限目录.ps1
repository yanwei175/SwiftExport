#Requires -Version 5.1
<#
.SYNOPSIS
从 WebApi 已有配置显式同步权限目录，不创建账号、修改授权或迁移数据库结构。
.PARAMETER ConfigurationPath
基础配置文件，默认使用仓库内 WebApi 的 appsettings.json；可指定服务器私有配置。
.PARAMETER EnvironmentName
叠加同目录 appsettings.{环境}.json；默认使用宿主环境变量，未配置时使用 Production。
.PARAMETER DryRun
只输出同步计划，不写入数据库。
#>
[CmdletBinding()]
param(
    [string]$ConfigurationPath,
    [string]$EnvironmentName,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if ([string]::IsNullOrWhiteSpace($ConfigurationPath)) {
    $ConfigurationPath = Join-Path $taskRepository 'SwiftExport.WebApi\appsettings.json'
}
$taskSettingsPath = (Resolve-Path -LiteralPath $ConfigurationPath).ProviderPath
$taskSettings = Get-Content -LiteralPath $taskSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$taskConnection = $taskSettings.ConnectionStrings.Default
if ([string]::IsNullOrWhiteSpace($EnvironmentName)) {
    $EnvironmentName = $env:DOTNET_ENVIRONMENT
    if (-not [string]::IsNullOrWhiteSpace($env:ASPNETCORE_ENVIRONMENT)) { $EnvironmentName = $env:ASPNETCORE_ENVIRONMENT }
    if ([string]::IsNullOrWhiteSpace($EnvironmentName)) { $EnvironmentName = 'Production' }
}
# 与后端连接配置一样，环境配置覆盖基础配置，标准连接环境变量最后覆盖。
$taskOverlayPath = Join-Path (Split-Path $taskSettingsPath -Parent) "appsettings.$EnvironmentName.json"
if (Test-Path -LiteralPath $taskOverlayPath) {
    $taskOverlay = Get-Content -LiteralPath $taskOverlayPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($null -ne $taskOverlay.ConnectionStrings.Default) { $taskConnection = $taskOverlay.ConnectionStrings.Default }
}
if ($null -ne $env:ConnectionStrings__Default) { $taskConnection = $env:ConnectionStrings__Default }
if ([string]::IsNullOrWhiteSpace($taskConnection)) { throw '现有配置缺少 ConnectionStrings.Default，请先配置后端数据库连接。' }

# 仅显示非敏感目标信息；已有 DbSeed 参数通过当前进程临时传递，执行后原样恢复。
$taskBuilder = New-Object System.Data.Common.DbConnectionStringBuilder
$taskBuilder.set_ConnectionString($taskConnection)
Write-Output "权限目录同步目标：$($taskBuilder['Host']):$($taskBuilder['Port'])/$($taskBuilder['Database'])；环境：$EnvironmentName。"
$taskPreviousConnection = [Environment]::GetEnvironmentVariable('SWIFTEXPORT_DBSEED_CONNECTION', 'Process')
try {
    [Environment]::SetEnvironmentVariable('SWIFTEXPORT_DBSEED_CONNECTION', $taskConnection, 'Process')
    $taskArguments = @('run', '--project', (Join-Path $PSScriptRoot 'DbSeed.csproj'), '--no-restore', '--', '--sync-permissions')
    if ($DryRun) { $taskArguments += '--dry-run' }
    & dotnet @taskArguments
    if ($LASTEXITCODE -ne 0) { throw "权限目录同步失败（退出码 $LASTEXITCODE）。" }
}
finally {
    if ($null -eq $taskPreviousConnection) {
        Remove-Item -LiteralPath Env:SWIFTEXPORT_DBSEED_CONNECTION -ErrorAction SilentlyContinue
    }
    else {
        [Environment]::SetEnvironmentVariable('SWIFTEXPORT_DBSEED_CONNECTION', $taskPreviousConnection, 'Process')
    }
}
