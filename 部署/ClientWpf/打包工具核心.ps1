#Requires -Version 5.1
# 负责发布参数、版本递增及本机设置，不编译客户端或操作服务器。
Set-StrictMode -Version Latest

function Assert-ClientPackHttpsAddress([string]$Address) {
    # 固定地址不允许携带账号、Token、查询或片段，不降低 HTTPS 信任要求。
    $parsed = $null
    if (-not [Uri]::TryCreate($Address, [UriKind]::Absolute, [ref]$parsed) -or
        $parsed.Scheme -ne 'https' -or $parsed.UserInfo -or $parsed.Query -or $parsed.Fragment) {
        throw 'API 和更新地址必须是无凭据、查询参数及片段的 HTTPS 地址。'
    }
}

function ConvertTo-ClientPackVersion([string]$Version) {
    # 发布工具仅处理三个非负整数，不将预发布或四段版本误提升为正式版。
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号格式应为三个非负整数，例如 0.1.3。' }
    $parsed = [version]$Version
    return $parsed
}

function Get-ClientPackLatestVersion([string]$ReleaseRoot) {
    # 索引是成功发布版本的依据；保留旧包不会使建议版本回退。
    $feedPath = Join-Path $ReleaseRoot 'releases.win.json'
    if (-not (Test-Path -LiteralPath $feedPath)) { return $null }
    $feed = Get-Content -LiteralPath $feedPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $latest = $null
    foreach ($asset in $feed.Assets) {
        if ($asset.PackageId -ne 'SwiftExportClient' -or $asset.Type -ne 'Full') { continue }
        $version = ConvertTo-ClientPackVersion $asset.Version
        if ($null -eq $latest -or $version -gt $latest) { $latest = $version }
    }
    return $latest
}

function Get-ClientPackSuggestedVersion([string]$ReleaseRoot, [string]$InitialVersion) {
    # 初次沿用项目版本，之后递增补丁号；失败残留包也占用版本，避免连续覆盖同名文件。
    $latest = Get-ClientPackLatestVersion $ReleaseRoot
    if (Test-Path -LiteralPath $ReleaseRoot) {
        foreach ($package in Get-ChildItem -LiteralPath $ReleaseRoot -File -Filter 'SwiftExportClient-*.nupkg') {
            if ($package.Name -notmatch '^SwiftExportClient-(\d+\.\d+\.\d+)-(?:full|delta)\.nupkg$') { continue }
            $attemptedVersion = ConvertTo-ClientPackVersion $Matches[1]
            if ($null -eq $latest -or $attemptedVersion -gt $latest) { $latest = $attemptedVersion }
        }
    }
    if ($null -eq $latest) { return (ConvertTo-ClientPackVersion $InitialVersion).ToString(3) }
    if ($latest.Build -eq [int]::MaxValue) { throw '补丁版本已达到上限，请手动提升主版本或次版本。' }
    return '{0}.{1}.{2}' -f $latest.Major, $latest.Minor, ($latest.Build + 1)
}

function Get-ClientPackSettings([string]$ClientRoot, [string]$SettingsPath) {
    # 没有本机设置时读取客户端真实配置，换电脑后不依赖开发者个人目录。
    $source = Get-Content -LiteralPath (Join-Path $ClientRoot 'appsettings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $settings = [ordered]@{
        UpdateUrl = [string]$source.Updates.FeedUrl
        ApiUrl = [string]$source.Api.BaseUrl
        InitialVersion = [string]$source.Client.Version
    }
    if (Test-Path -LiteralPath $SettingsPath) {
        $saved = Get-Content -LiteralPath $SettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $settings.UpdateUrl = [string]$saved.UpdateUrl
        $settings.ApiUrl = [string]$saved.ApiUrl
    }
    return [pscustomobject]$settings
}

function Save-ClientPackSettings([string]$SettingsPath, [string]$UpdateUrl, [string]$ApiUrl) {
    # 只保存已校验的地址；先写临时文件再替换，避免中断留下半份 JSON。
    Assert-ClientPackHttpsAddress $UpdateUrl
    Assert-ClientPackHttpsAddress $ApiUrl
    [void][IO.Directory]::CreateDirectory((Split-Path $SettingsPath -Parent))
    $temporaryPath = $SettingsPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [ordered]@{ UpdateUrl = $UpdateUrl.TrimEnd('/') + '/'; ApiUrl = $ApiUrl.TrimEnd('/') } |
            ConvertTo-Json | Set-Content -LiteralPath $temporaryPath -Encoding UTF8
        Move-Item -LiteralPath $temporaryPath -Destination $SettingsPath -Force
    }
    finally { if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath } }
}

function Invoke-ClientPackDotnet([string[]]$Arguments, [string]$FailureMessage) {
    # PowerShell 5.1 的外部程序输出不会自动进入 Transcript，显式转入 Host 并保留退出码。
    Get-Command dotnet -ErrorAction Stop | Out-Null
    $previousPreference = $ErrorActionPreference
    try {
        # 标准错误也可能包含非致命提示，失败依据仍为 dotnet 的退出码。
        $ErrorActionPreference = 'Continue'
        & dotnet @Arguments 2>&1 | ForEach-Object { Write-Host ([string]$_) }
        $commandExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousPreference }
    if ($commandExitCode -ne 0) { throw $FailureMessage }
}
