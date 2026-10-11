#Requires -Version 5.1
# 负责交互式一键打包及无交互入口，沿用同一发布脚本，不上传服务器。
[CmdletBinding()]
param(
    [string]$Version,
    [string]$UpdateUrl,
    [string]$ApiUrl,
    [string]$Notes,
    [switch]$NonInteractive,
    [switch]$NoOpenFolder
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '打包工具核心.ps1')

function Read-ClientPackDefault([string]$Label, [string]$DefaultValue) {
    # 用户回车即可沿用默认值，空值不意外覆盖已有发布设置。
    $inputValue = Read-Host "$Label [$DefaultValue]"
    if ([string]::IsNullOrWhiteSpace($inputValue)) { return $DefaultValue }
    return $inputValue.Trim()
}

# 分开源码、生成产物及本机设置，清理客户端 bin 不会丢失发布历史。
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$clientRoot = Join-Path $repositoryRoot 'SwiftExport.Client.Wpf'
$workRoot = Join-Path $repositoryRoot 'artifacts/部署/ClientWpf'
$releaseRoot = Join-Path $workRoot 'Releases'
$settingsPath = Join-Path $PSScriptRoot '../本机配置/ClientWpf/打包设置.json'
$transcribing = $false
$logPath = $null
try {
    $settings = Get-ClientPackSettings $clientRoot $settingsPath
    if (-not $Version) { $Version = Get-ClientPackSuggestedVersion $releaseRoot $settings.InitialVersion }
    if (-not $UpdateUrl) { $UpdateUrl = $settings.UpdateUrl }
    if (-not $ApiUrl) { $ApiUrl = $settings.ApiUrl }
    if (-not $NonInteractive) {
        Write-Host 'SwiftExport 客户端一键打包' -ForegroundColor Cyan
        Write-Host '回车沿用默认值，Ctrl+C 取消。修改客户端代码后直接运行此工具即可。'
        $Version = Read-ClientPackDefault '发布版本' $Version
        $UpdateUrl = Read-ClientPackDefault '更新地址' $UpdateUrl
        $ApiUrl = Read-ClientPackDefault 'API 地址' $ApiUrl
        if (-not $Notes) { $Notes = Read-Host '本次更新说明（可留空）' }
    }
    $Version = (ConvertTo-ClientPackVersion $Version).ToString(3)
    Assert-ClientPackHttpsAddress $UpdateUrl
    Assert-ClientPackHttpsAddress $ApiUrl
    $latest = Get-ClientPackLatestVersion $releaseRoot
    if ($null -ne $latest -and [version]$Version -le $latest) { throw "版本必须大于已发布版本 $latest。" }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '未找到 dotnet，请在开发电脑安装 .NET 10 SDK。' }
    Save-ClientPackSettings $settingsPath $UpdateUrl $ApiUrl
    $runId = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    $logRoot = Join-Path $workRoot 'Logs'
    [void][IO.Directory]::CreateDirectory($logRoot)
    $logPath = Join-Path $logRoot "$Version-$runId.log"
    Start-Transcript -LiteralPath $logPath -Force | Out-Null
    $transcribing = $true
    $arguments = @{ Version = $Version; UpdateUrl = $UpdateUrl; ApiUrl = $ApiUrl }
    if (-not [string]::IsNullOrWhiteSpace($Notes)) {
        $notesRoot = Join-Path $workRoot 'Notes'
        [void][IO.Directory]::CreateDirectory($notesRoot)
        $notesPath = Join-Path $notesRoot "$Version-$runId.md"
        $Notes | Set-Content -LiteralPath $notesPath -Encoding UTF8
        $arguments.ReleaseNotes = $notesPath
    }
    Write-Host "开始打包 $Version；API：$ApiUrl；更新源：$UpdateUrl"
    & (Join-Path $PSScriptRoot '打包客户端.ps1') @arguments
    Write-Host "打包成功：$releaseRoot" -ForegroundColor Green
    Write-Host "日志：$logPath"
    if (-not $NoOpenFolder) {
        # 用户启动的交互工具完成后展示产物，浏览目录失败不改变打包成功结果。
        try { Start-Process explorer.exe -ArgumentList @('"' + $releaseRoot + '"') }
        catch { Write-Warning "输出目录无法自动打开，请手动打开：$releaseRoot" }
    }
}
catch {
    Write-Host "打包失败：$($_.Exception.Message)" -ForegroundColor Red
    if ($logPath) { Write-Host "详细日志：$logPath" }
    exit 1
}
finally { if ($transcribing) { Stop-Transcript | Out-Null } }
