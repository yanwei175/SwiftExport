#Requires -Version 5.1
<#! 在开发电脑创建离线部署 ZIP；保留 WebApi/公共 相对布局，只收录白名单工具和官方 Caddy。 !#>
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$toolRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repository = Split-Path $toolRoot -Parent
if (!(Test-Path -LiteralPath (Join-Path $repository 'SwiftExport.WebApi/SwiftExport.WebApi.csproj'))) { throw '请在开发电脑的源码仓库运行工具打包；服务器使用解压后的部署入口。' }
. (Join-Path $toolRoot '公共/Caddy/Caddy托管核心.ps1')
$binary = Get-VerifiedCaddyBinary
$output = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::GetExtension($output) -ne '.zip' -or (Test-Path -LiteralPath $output)) { throw '请选择不存在的新 ZIP 文件路径。' }
$stage = Join-Path $repository ('artifacts/部署/WebApi/工具包暂存/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($stage)
# 不按目录整体复制，避免把本机设置、生产 JSON、证书或测试 artifacts 带到服务器。
$names = @(
    'WebApi/启动后端部署工具.cmd','WebApi/管理员启动窗口.ps1','WebApi/deployment-launcher.ps1',
    'WebApi/后端部署窗口.ps1','WebApi/后端部署核心.ps1','WebApi/启动Caddy首次安装.cmd',
    'WebApi/Caddy首次安装窗口.ps1','WebApi/首次安装Caddy托管.ps1','WebApi/首次托管核心.ps1',
    'WebApi/清理旧IIS部署.ps1','WebApi/打包部署工具.ps1','WebApi/打包后端.ps1',
    'WebApi/Caddy首次安装与日常更新.md','WebApi/可视化发布与后端部署工具使用说明.md',
    'WebApi/Windows文件夹发布配置模板.xml','WebApi/服务器配置示例.json',
    '公共/Caddy/Caddy托管核心.ps1','公共/Caddy/获取Caddy.ps1','公共/证书/安装客户端根证书.ps1',
    'PostgreSQL/每日备份.ps1','PostgreSQL/注册每日备份任务.ps1','PostgreSQL/备份设置示例.json','PostgreSQL/使用说明.md'
)
foreach ($name in $names) {
    $target = Join-Path $stage $name
    [void][IO.Directory]::CreateDirectory((Split-Path $target -Parent))
    Copy-Item -LiteralPath (Join-Path $toolRoot $name) -Destination $target
}
$binaryRoot = Join-Path $stage '公共/Caddy/bin'
[void][IO.Directory]::CreateDirectory($binaryRoot)
Copy-Item -LiteralPath $binary -Destination (Join-Path $binaryRoot 'caddy.exe')
foreach ($name in @('LICENSE','README.md')) { Copy-Item -LiteralPath (Join-Path $toolRoot ('公共/Caddy/bin/' + $name)) -Destination (Join-Path $binaryRoot $name) }
# 使用说明导出到 ZIP 根时修正相对链接，源码中的说明仍从 WebApi 目录阅读。
$guide = Get-Content -LiteralPath (Join-Path $PSScriptRoot '服务器工具包使用说明.md') -Raw -Encoding UTF8
$guide = $guide.Replace('](Caddy首次安装与日常更新.md)', '](WebApi/Caddy首次安装与日常更新.md)')
$guide = $guide.Replace('](../PostgreSQL/使用说明.md)', '](PostgreSQL/使用说明.md)')
[IO.File]::WriteAllText((Join-Path $stage '使用说明.md'), $guide, [Text.Encoding]::UTF8)
[IO.File]::WriteAllText((Join-Path $stage '工具版本.txt'), "Caddy 2.11.7 Windows x64`r`nEXE SHA256: $script:CaddyBinaryHash`r`n窗口入口位于 WebApi；必须保留同级公共目录。`r`n本 ZIP 不包含后端发布包、真实配置、证书包或 CA。`r`n", [Text.Encoding]::UTF8)
[void][IO.Directory]::CreateDirectory((Split-Path $output -Parent))
Compress-Archive -LiteralPath @((Get-ChildItem -LiteralPath $stage -Force).FullName) -DestinationPath $output -CompressionLevel Optimal
Write-Output "部署工具已打包：$output。完整解压后打开 WebApi 目录中的启动入口。"
