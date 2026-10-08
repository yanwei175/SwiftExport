#Requires -Version 5.1
<#! 在开发电脑生成新发布目录，移除开发环境配置，防止把本机连接字符串带到服务器。 !#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateSet('win-x64', 'linux-x64')][string]$RuntimeIdentifier
)
$ErrorActionPreference = 'Stop'
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) { throw '请使用不存在的新目录，避免混入旧版本文件或配置。' }
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$project = Join-Path $repository 'SwiftExport.WebApi\SwiftExport.WebApi.csproj'
# 指定平台时由 publish 还原对应 RID 的资产，生成依赖服务器 .NET 10 运行时的包；省略参数保留既有打包行为。
$publishArguments = @('publish', $project, '-c', 'Release', '-o', $outputPath)
if ($RuntimeIdentifier) {
    $publishArguments += @('-r', $RuntimeIdentifier, '--self-contained', 'false')
} else {
    $publishArguments += '--no-restore'
}
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw '发布失败，不能将此目录作为可部署包。' }
# 只删除这次 publish 输出根目录内的配置文件，不递归删除，不改源码配置。
Get-ChildItem -LiteralPath $outputPath -Filter 'appsettings*.json' -File | ForEach-Object {
    Remove-Item -LiteralPath $_.FullName
}
Set-Content -LiteralPath (Join-Path $outputPath 'appsettings.json') -Value '{}' -Encoding UTF8
Write-Output "发布目录已生成：$outputPath。生产配置由首次部署时单独提供。"
