#Requires -Version 5.1
# 验证真实 ZIP 的完整性与解压后独立运行；只写隔离产物，不注册服务或访问数据库。
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$workspace = Join-Path $repositoryRoot ('artifacts/部署/验证/工具包 中文 空格/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($workspace)
$zipPath = Join-Path $workspace '离线工具.zip'
Push-Location $workspace
try {
    # 从非仓库工作目录调用源入口，确保不依赖 Get-Location 定位源码。
    & (Join-Path $PSScriptRoot '打包部署工具.ps1') -OutputPath $zipPath
    $unpacked = Join-Path $workspace '独立服务器工具'
    Expand-Archive -LiteralPath $zipPath -DestinationPath $unpacked
    $files = @(Get-ChildItem -LiteralPath $unpacked -File -Recurse)
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($unpacked.Length + 1).Replace('\', '/')
        if ($relative -match '(本机配置|appsettings\.Production|\.cer$|\.pfx$|验证.*\.ps1$|CaddyData|\.pack\.lock)') { throw "工具包包含本机或测试文件：$relative" }
    }
    foreach ($name in @('使用说明.md','工具版本.txt','WebApi/后端部署核心.ps1','WebApi/deployment-launcher.ps1','公共/证书/安装客户端根证书.ps1','公共/Caddy/bin/LICENSE','公共/Caddy/bin/README.md')) {
        if (!(Test-Path -LiteralPath (Join-Path $unpacked $name))) { throw "工具包漏项：$name" }
    }
    . (Join-Path $unpacked '公共/Caddy/Caddy托管核心.ps1')
    $binary = Get-VerifiedCaddyBinary
    if ($binary -ne (Join-Path $unpacked '公共/Caddy/bin/caddy.exe')) { throw '解压工具引用了源码目录中的 Caddy。' }
    foreach ($name in @('后端部署窗口.ps1', 'Caddy首次安装窗口.ps1')) {
        & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -STA -ExecutionPolicy Bypass -File (Join-Path $unpacked ('WebApi/' + $name)) -ValidateLayout
        if ($LASTEXITCODE -ne 0) { throw "解压后窗口初始化失败：$name" }
    }
    if (Test-Path -LiteralPath (Join-Path $unpacked '本机配置')) { throw '窗口布局验证不应生成或改写服务器设置。' }
    Write-Output "工具 ZIP 独立运行验证通过：$($files.Count) 个文件；$zipPath"
} finally { Pop-Location }
