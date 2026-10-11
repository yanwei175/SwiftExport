#Requires -Version 5.1
# 负责生成可首次安装及增量更新的本地发布目录；不上传、不修改服务器或证书。
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$UpdateUrl,
    [string]$ApiUrl,
    [string]$ReleaseNotes
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '打包工具核心.ps1')
Assert-ClientPackHttpsAddress $UpdateUrl
if ($ApiUrl) { Assert-ClientPackHttpsAddress $ApiUrl }
$Version = (ConvertTo-ClientPackVersion $Version).ToString(3)
if ($ReleaseNotes -and -not (Test-Path -LiteralPath $ReleaseNotes -PathType Leaf)) { throw '更新说明文件不存在。' }
$resolvedReleaseNotes = if ($ReleaseNotes) { (Resolve-Path -LiteralPath $ReleaseNotes).Path } else { $null }
# 项目与产物从部署入口定位，不依赖调用者的工作目录。
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$clientRoot = Join-Path $repositoryRoot 'SwiftExport.Client.Wpf'
$workRoot = Join-Path $repositoryRoot 'artifacts/部署/ClientWpf'
$releaseRoot = Join-Path $workRoot 'Releases'
$publishRoot = Join-Path $workRoot "publish/$Version-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
# 独占锁覆盖校验、编译和打包，避免两个入口同时改写安装器与发布索引。
try { $packLock = [IO.File]::Open((Join-Path $releaseRoot '.pack.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
catch [IO.IOException] { throw '另一个客户端打包正在运行，请等待完成后再试。' }
try {
    if (Test-Path -LiteralPath (Join-Path $releaseRoot "SwiftExportClient-$Version-full.nupkg")) {
        throw '此版本已经打包，请使用更大的版本号，避免覆盖用户正在下载的包。'
    }
    $latest = Get-ClientPackLatestVersion $releaseRoot
    if ($null -ne $latest -and [version]$Version -le $latest) { throw '版本号必须大于已有发布版本。' }
    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
    # vpk 通过当前部署目录的 .config 清单发现；publish 使用项目绝对路径。
    Push-Location $PSScriptRoot
    try {
        Invoke-ClientPackDotnet @('tool', 'restore', '--tool-manifest', (Join-Path $PSScriptRoot '.config/dotnet-tools.json')) 'Velopack 工具还原失败。'
        # 自包含 x64 发布使内网终端无需另行下载 .NET Desktop Runtime；WPF 不裁剪。
        Invoke-ClientPackDotnet @('publish', (Join-Path $clientRoot 'SwiftExport.Client.Wpf.csproj'),
            '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishTrimmed=false',
            '-p:PublishSingleFile=false', "-p:Version=$Version", '-o', $publishRoot) '客户端发布失败。'
        $settingsPath = Join-Path $publishRoot 'appsettings.json'
        $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $settings.Updates.FeedUrl = $UpdateUrl.TrimEnd('/') + '/'
        $settings.Client.Version = $Version
        if ($ApiUrl) { $settings.Api.BaseUrl = $ApiUrl.TrimEnd('/') }
        $settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settingsPath -Encoding utf8
        # 说明副本不随安装包分发，避免用户误把它当作运行配置。
        $notesCopy = Join-Path $publishRoot 'appsettings - Note.json'
        if (Test-Path -LiteralPath $notesCopy) { Remove-Item -LiteralPath $notesCopy }
        $packArguments = @('pack', '--packId', 'SwiftExportClient', '--packVersion', $Version,
            '--packDir', $publishRoot, '--mainExe', 'SwiftExport.Client.Wpf.exe',
            '--packTitle', 'SwiftExport', '--channel', 'win', '--runtime', 'win-x64', '--outputDir', $releaseRoot)
        if ($resolvedReleaseNotes) { $packArguments += @('--releaseNotes', $resolvedReleaseNotes) }
        Invoke-ClientPackDotnet (@('tool', 'run', 'vpk', '--') + $packArguments) 'Velopack 打包失败。'
        Write-Host "发布文件已生成：$releaseRoot"
        Write-Host '先上传新的 nupkg 和 Setup 文件，再原子替换 releases.win.json；保留旧包。'
    }
    finally { Pop-Location }
}
finally { $packLock.Dispose() }
