#Requires -Version 5.1
# 使用隔离文件夹验证打包默认值、版本、参数及失败收尾；不调用真实编译或服务器。
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$deploymentRoot = Join-Path $repositoryRoot 'SwiftExport.Client.Wpf/Deployment'
. (Join-Path $deploymentRoot '打包工具核心.ps1')
$fixtureRoot = Join-Path $repositoryRoot ('artifacts/client-pack-tests/' + [Guid]::NewGuid().ToString('N'))
$releaseRoot = Join-Path $fixtureRoot 'bin/Velopack/Releases'
$settingsPath = Join-Path $fixtureRoot 'bin/Velopack/打包设置.json'
[void][IO.Directory]::CreateDirectory($releaseRoot)
$script:checkCount = 0

function Assert-PackEqual($Expected, $Actual, [string]$Message) {
    if ($Expected -ne $Actual) { throw "$Message；预期：$Expected，实际：$Actual" }
    $script:checkCount++
}
function Assert-PackRejects([scriptblock]$Action, [string]$ExpectedMessage) {
    # 必须由被测行为抛出异常，不把测试自身的失败当作预期异常。
    $failure = $null
    try { & $Action | Out-Null } catch { $failure = $_.Exception.Message }
    if ($null -eq $failure -or $failure -notlike "*$ExpectedMessage*") { throw "未按预期拒绝操作：$ExpectedMessage；实际：$failure" }
    $script:checkCount++
}

@{ Api = @{ BaseUrl = 'https://default.example' }; Updates = @{ FeedUrl = 'https://default.example/client-updates/' };
   Client = @{ Version = '0.1.0' } } | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'appsettings.json') -Encoding UTF8
$settings = Get-ClientPackSettings $fixtureRoot $settingsPath
Assert-PackEqual 'https://default.example/client-updates/' $settings.UpdateUrl '首次读取源码更新地址'
Assert-PackEqual '0.1.0' (Get-ClientPackSuggestedVersion $releaseRoot $settings.InitialVersion) '首次版本沿用配置'
Save-ClientPackSettings $settingsPath 'https://saved.example/updates' 'https://saved.example/'
$saved = Get-ClientPackSettings $fixtureRoot $settingsPath
Assert-PackEqual 'https://saved.example/updates/' $saved.UpdateUrl '保存及规范化更新地址'
Assert-PackEqual 'https://saved.example' $saved.ApiUrl 'API 地址沿用上次设置'
Assert-PackRejects { Save-ClientPackSettings $settingsPath 'http://unsafe.example' 'https://saved.example' } 'HTTPS'
Assert-PackEqual 'https://saved.example/updates/' (Get-ClientPackSettings $fixtureRoot $settingsPath).UpdateUrl '失败不得覆盖已有设置'
foreach ($address in @('https://user:secret@example.com/updates/', 'https://example.com/?token=secret', 'https://example.com/#fragment')) {
    Assert-PackRejects { Assert-ClientPackHttpsAddress $address } 'HTTPS'
}
foreach ($version in @('0.1', '0.1.2.3', '0.1.2-beta', '-1.1.1')) {
    Assert-PackRejects { ConvertTo-ClientPackVersion $version } '三个非负整数'
}
$feedPath = Join-Path $releaseRoot 'releases.win.json'
@{ Assets = @(
    @{ PackageId = 'SwiftExportClient'; Version = '0.9.9'; Type = 'Full' },
    @{ PackageId = 'SwiftExportClient'; Version = '0.1.0'; Type = 'Full' },
    @{ PackageId = 'OtherApp'; Version = '99.0.0'; Type = 'Full' }
) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $feedPath -Encoding UTF8
Assert-PackEqual '0.9.10' (Get-ClientPackSuggestedVersion $releaseRoot '0.1.0') '乱序索引和其他应用不影响递增'
Set-Content -LiteralPath (Join-Path $releaseRoot 'SwiftExportClient-0.9.12-full.nupkg') -Value '失败残留'
Assert-PackEqual '0.9.13' (Get-ClientPackSuggestedVersion $releaseRoot '0.1.0') '失败残留包的版本不能覆盖'

# 在假客户端目录运行真实核心脚本，校验失败发生在原生命令之前。
$fakeDeployment = Join-Path $fixtureRoot 'Deployment'
[void][IO.Directory]::CreateDirectory($fakeDeployment)
foreach ($name in @('打包客户端.ps1', '打包工具核心.ps1')) {
    Copy-Item -LiteralPath (Join-Path $deploymentRoot $name) -Destination (Join-Path $fakeDeployment $name)
}
$packScript = Join-Path $fakeDeployment '打包客户端.ps1'
$packArguments = @{ Version = '0.9.10'; UpdateUrl = 'https://example.com/updates/'; ApiUrl = 'https://example.com' }
$lockPath = Join-Path $releaseRoot '.pack.lock'
$heldLock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try { Assert-PackRejects { & $packScript @packArguments } '另一个客户端打包' }
finally { $heldLock.Dispose() }
$packArguments.Version = '0.9.9'
Assert-PackRejects { & $packScript @packArguments } '版本号必须大于'
$packArguments.Version = '0.9.10'
$feedBefore = Get-Content -LiteralPath $feedPath -Raw -Encoding UTF8
function dotnet {
    # 模拟工具还原成功但编译失败，防止继续调用 vpk 或误报成功。
    if ($args[0] -eq 'publish') {
        Write-Error '模拟编译失败标准错误' -ErrorAction Continue
        $global:LASTEXITCODE = 17
    }
    else { Write-Output '模拟工具还原成功'; $global:LASTEXITCODE = 0 }
}
try {
    $nativeLogPath = Join-Path $fixtureRoot 'native-output.log'
    Start-Transcript -LiteralPath $nativeLogPath | Out-Null
    Assert-PackRejects { & $packScript @packArguments } '客户端发布失败'
    Stop-Transcript | Out-Null
    if ((Get-Content -LiteralPath $nativeLogPath -Raw -Encoding UTF8) -notlike '*模拟编译失败标准错误*') { throw '标准错误未进入日志。' }
    $script:checkCount++
    Assert-PackEqual $feedBefore (Get-Content -LiteralPath $feedPath -Raw -Encoding UTF8) '编译失败不改写发布索引'
    $releasedLock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $releasedLock.Dispose()
    $script:checkCount++
}
finally { Remove-Item Function:\dotnet }
Write-Host "打包工具验证通过：$script:checkCount 项。隔离夹具：$fixtureRoot"
