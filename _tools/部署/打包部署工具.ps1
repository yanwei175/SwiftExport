#Requires -Version 5.1
<#! 在开发电脑创建可离线复制的部署工具 ZIP；只收录固定工具和官方 Caddy，不包含服务器配置或 CA。 !#>
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Caddy托管核心.ps1')
$binary = Get-VerifiedCaddyBinary
$output = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::GetExtension($output) -ne '.zip' -or (Test-Path -LiteralPath $output)) { throw '请选择不存在的新 ZIP 文件路径。' }
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$stage = Join-Path $repository ('artifacts/deployment-packages/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($stage)
[void][IO.Directory]::CreateDirectory((Join-Path $stage 'bin'))
# 使用白名单，禁止把窗口设置、生产 JSON、证书、私钥或测试 artifacts 一起打包。
$names = @(
    '启动后端部署工具.cmd','管理员启动窗口.ps1','deployment-launcher.ps1','后端部署窗口.ps1','后端部署核心.ps1',
    '启动Caddy首次安装.cmd','Caddy首次安装窗口.ps1','首次安装Caddy托管.ps1',
    '首次托管核心.ps1','Caddy托管核心.ps1','安装客户端根证书.ps1','清理旧IIS部署.ps1',
    '获取Caddy.ps1','打包部署工具.ps1','打包后端.ps1',
    'Caddy首次安装与日常更新.md','可视化发布与后端部署工具使用说明.md',
    'Windows文件夹发布配置模板.xml','服务器配置示例.json'
)
foreach ($name in $names) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage }
Copy-Item -LiteralPath $binary -Destination (Join-Path $stage 'bin/caddy.exe')
foreach ($name in @('LICENSE','README.md')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('bin/' + $name)) -Destination (Join-Path $stage ('bin/' + $name)) }
[IO.File]::WriteAllText((Join-Path $stage '工具版本.txt'), "Caddy 2.11.7 Windows x64`r`nEXE SHA256: $script:CaddyBinaryHash`r`n首次安装和日常更新入口详见 Caddy首次安装与日常更新.md`r`n本 ZIP 不包含后端发布包、真实生产配置或 CA。`r`n", [Text.Encoding]::UTF8)
[void][IO.Directory]::CreateDirectory((Split-Path $output -Parent))
Compress-Archive -LiteralPath @((Get-ChildItem -LiteralPath $stage -Force).FullName) -DestinationPath $output -CompressionLevel Optimal
Write-Output "部署工具已打包：$output。后端仍需在 Visual Studio 单独发布。"
