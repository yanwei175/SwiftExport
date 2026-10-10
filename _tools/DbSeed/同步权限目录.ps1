#Requires -Version 5.1
<#
.SYNOPSIS
从代码权限目录离线生成 PostgreSQL 同步 SQL；不读连接配置，不自动执行 SQL。
.PARAMETER OutputPath
SQL 输出文件；默认保存到本工具目录的权限目录同步.sql。
#>
[CmdletBinding()]
param(
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot '权限目录同步.sql'
}
# 隔离编译产物，避免覆盖正在运行的 API 所使用的 DLL；此路径在现有 bin 忽略范围内。
$taskArtifactsPath = Join-Path $PSScriptRoot 'bin\SQL生成构建'
$taskArguments = @('run', '--project', (Join-Path $PSScriptRoot 'DbSeed.csproj'),
    '--artifacts-path', $taskArtifactsPath, '--no-restore', '--', '--export-permissions-sql', $OutputPath)
& dotnet @taskArguments
if ($LASTEXITCODE -ne 0) { throw "权限目录 SQL 生成失败（退出码 $LASTEXITCODE）；请先按说明还原本工具的隔离构建依赖。" }
