#Requires -Version 5.1
<#! 以 SCM/IIS/网络替身验证首次安装和恢复，所有文件落在仓库 artifacts，不改变系统服务或信任库。 !#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '首次托管核心.ps1')
$realIisPreflight = (Get-Command Get-IisCutoverState).ScriptBlock
$workspace = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) ('artifacts/部署/验证/hosting-tests/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($workspace)
$script:FailureStage = ''
$script:Backend = $null
$script:Caddy = $null
$script:Firewall = $false
$script:Events = @()
$script:Iis = $null
$script:IisAutoStarts = @{}

# 测试替身只记录行为；不调用原生服务、IIS、防火墙或 ACL 工具。
function Assert-BackendPrivileges([string]$Mode) { }
function Get-NetIPAddress { param($AddressFamily); [PSCustomObject]@{ IPAddress = '192.0.2.12' } }
function Get-NetTCPConnection { param($State,$LocalPort,$ErrorAction); return $null }
function Get-NetFirewallRule { param($Name,$ErrorAction); return $null }
function Get-IisCutoverState([string]$Name,[string]$Address) { return $script:Iis }
function Get-Service { param($Name,$ErrorAction); if ($Name -eq 'SwiftExport_WebApi') { return $script:Backend }; return $script:Caddy }
function Stop-Website { param($Name); $script:Events += 'StopSite' }
function Stop-WebAppPool { param($Name); $script:Events += 'StopPool' }
function Start-Website { param($Name); $script:Events += 'StartSite' }
function Start-WebAppPool { param($Name); $script:Events += 'StartPool' }
function Set-ItemProperty { param($LiteralPath,$Name,$Value); $script:IisAutoStarts[$Name] = $Value; $script:Events += 'Set:' + $Name + ':' + $Value }
function Stop-Service { param($Name,$ErrorAction); $script:Events += 'StopCaddy' }
function sc.exe {
    param($Action,$Name)
    if ($Action -ne 'delete' -or $Name -notin @('SwiftExport_WebApi','SwiftExport_Caddy')) { throw '测试意外服务命令。' }
    $script:Events += 'Delete:' + $Name
    if ($Name -eq 'SwiftExport_WebApi') { $script:Backend = $null } else { $script:Caddy = $null }
    $global:LASTEXITCODE = 0
}
function Invoke-BackendDeployment {
    param($PackagePath,$InstallPath,$Mode,$ListenUrl,$ProductionConfigurationPath,$Progress)
    $script:Events += 'InstallBackend'
    if ($script:FailureStage -eq 'Backend') { throw '模拟后端安装失败（自身已清理）' }
    [void][IO.Directory]::CreateDirectory($InstallPath)
    $script:Backend = [PSCustomObject]@{ Status = 'Running' }
    return [PSCustomObject]@{ InstallPath = $InstallPath }
}
function Stop-BackendInstance { param($InstallPath,$Mode,$State); $script:Events += 'StopBackend' }
function Install-CaddyService([string]$InstallPath,[string]$DataPath) {
    $script:Events += 'InstallCaddy'
    $script:Caddy = [PSCustomObject]@{ Status = 'Running' }
    $script:Caddy | Add-Member ScriptMethod WaitForStatus { param($Status,$Timeout) }
    $root = Join-Path $DataPath 'pki/authorities/local'
    [void][IO.Directory]::CreateDirectory($root)
    [IO.File]::WriteAllText((Join-Path $root 'root.crt'),'retained-public-test-fixture')
    [IO.File]::WriteAllText((Join-Path $root 'root.key'),'retained-private-test-fixture')
    if ($script:FailureStage -eq 'Caddy') { throw '模拟 Caddy 启动失败' }
}
function Export-CaddyRootCertificate([string]$DataPath,[string]$ExportPath) {
    [void][IO.Directory]::CreateDirectory($ExportPath)
    return [PSCustomObject]@{ Path = (Join-Path $ExportPath 'test.cer'); Thumbprint = 'TEST' }
}
function Wait-CaddyHttps([string]$Url,[string]$RootCertificatePath) {
    $script:Events += 'VerifyHttps'
    if ($script:FailureStage -eq 'Https') { throw '模拟 HTTPS 检查失败' }
}
function New-NetFirewallRule {
    param($Name,$DisplayName,$Direction,$Action,$Protocol,$LocalAddress,$LocalPort,$RemoteAddress,$Profile,$ErrorAction)
    if ($LocalPort -ne 443 -or $RemoteAddress -ne 'LocalSubnet') { throw '意外防火墙放行范围。' }
    $script:Firewall = $true
    $script:Events += 'AddFirewall'
    if ($script:FailureStage -eq 'State') {
        # 模拟最后写状态文件失败，验证已经创建的防火墙规则也会撤销。
        [void][IO.Directory]::CreateDirectory((Join-Path $script:CurrentCaddy '托管状态.json'))
    }
}
function Remove-NetFirewallRule { param($Name,$ErrorAction); $script:Firewall = $false; $script:Events += 'RemoveFirewall' }
function Assert-HostingTest([bool]$Condition,[string]$Message) { if (!$Condition) { throw "验证失败：$Message" } }

# 每个场景使用独立路径和无业务的发布夹具，生产配置只有假连接值。
function New-HostingFixture([string]$Name) {
    $root = Join-Path $workspace $Name
    $package = Join-Path $root 'package'
    $storage = Join-Path $root 'images'
    $old = Join-Path $root 'old-iis'
    foreach ($path in @($package,$storage,$old)) { [void][IO.Directory]::CreateDirectory($path) }
    foreach ($name in @('SwiftExport.WebApi.exe','SwiftExport.WebApi.dll','SwiftExport.WebApi.deps.json','SwiftExport.WebApi.runtimeconfig.json','Microsoft.Extensions.Hosting.WindowsServices.dll')) {
        [IO.File]::WriteAllText((Join-Path $package $name),'not-executable-fixture')
    }
    [IO.File]::WriteAllText((Join-Path $old 'marker.txt'),'old-iis-unchanged')
    $config = Join-Path $root 'production.json'
    @{ ConnectionStrings = @{ Default = 'Host=127.0.0.1;Database=never-opened' }; Auth = @{ JwtKey = 'test-only-at-least-32-byte-signing-key'; UseDevUser = $false }; FileStorage = @{ RootPath = $storage } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $config -Encoding UTF8
    $script:Iis = [PSCustomObject]@{ SiteName = 'test-site'; PoolName = 'test-pool'; PhysicalPath = $old; SiteWasStarted = $true; PoolWasStarted = $true; SiteAutoStart = $true; PoolAutoStart = $true }
    $script:Backend = $null; $script:Caddy = $null; $script:Firewall = $false; $script:Events = @()
    $script:IisAutoStarts = @{ serverAutoStart = $true; autoStart = $true }
    $script:CurrentCaddy = Join-Path $root 'caddy'
    return @{ PackagePath = $package; ProductionConfigurationPath = $config; ServerAddress = '192.0.2.12'; BackendInstallPath = (Join-Path $root 'backend'); CaddyInstallPath = $script:CurrentCaddy; CaddyDataPath = (Join-Path $root 'ca'); CertificateExportPath = (Join-Path $root 'export'); Progress = { param($Message) } }
}

$options = New-HostingFixture 'plan'
$script:FailureStage = ''
$plan = Invoke-CaddyFirstInstallation @options -PlanOnly
Assert-HostingTest ($script:Events.Count -eq 0 -and !(Test-Path -LiteralPath $options.CaddyInstallPath)) '预检不得创建目录或停止服务。'
Assert-HostingTest ($plan.ListenUrl -eq 'http://127.0.0.1:5081') '后端只能监听回环地址。'
Write-Output '通过：预检无托管副作用。'

# 配置不完整或含旧监听端点时，必须在第一次停止 IIS 前拒绝。
foreach ($invalid in @('SqlServer','ShortJwt','Kestrel')) {
    $options = New-HostingFixture ('invalid-' + $invalid)
    $configuration = Get-Content -LiteralPath $options.ProductionConfigurationPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($invalid -eq 'SqlServer') { $configuration.ConnectionStrings.Default = 'Server=old;Database=never-opened;Integrated Security=true' }
    if ($invalid -eq 'ShortJwt') { $configuration.Auth.JwtKey = 'too-short' }
    if ($invalid -eq 'Kestrel') { $configuration | Add-Member NoteProperty Kestrel @{ Endpoints = @{ OldHttps = @{ Url = 'https://0.0.0.0:443' } } } }
    $configuration | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $options.ProductionConfigurationPath -Encoding UTF8
    $threw = $false
    try { Invoke-CaddyFirstInstallation @options | Out-Null } catch { $threw = $true }
    Assert-HostingTest ($threw -and $script:Events.Count -eq 0) "$invalid 必须在停止旧程序之前拒绝。"
}
Write-Output '通过：旧 SQL Server、过短 JWT、显式 Kestrel 端点在预检拒绝。'

foreach ($stage in @('Backend','Caddy','Https','State')) {
    $options = New-HostingFixture $stage
    $script:FailureStage = $stage
    $threw = $false
    try { Invoke-CaddyFirstInstallation @options | Out-Null } catch { $threw = $true }
    Assert-HostingTest $threw "$stage 必须报告安装失败。"
    Assert-HostingTest ($null -eq $script:Backend -and $null -eq $script:Caddy -and !$script:Firewall) "$stage 必须撤销本次服务和防火墙规则。"
    Assert-HostingTest ($script:Events -contains 'StartSite' -and $script:Events -contains 'StartPool') "$stage 必须恢复旧 IIS。"
    Assert-HostingTest ($script:IisAutoStarts.serverAutoStart -and $script:IisAutoStarts.autoStart) "$stage 必须恢复 IIS 自动启动设置。"
    Assert-HostingTest ([IO.File]::ReadAllText((Join-Path $script:Iis.PhysicalPath 'marker.txt')) -eq 'old-iis-unchanged') '旧 IIS 文件不能被移动或改写。'
    if ($stage -ne 'Backend') { Assert-HostingTest (Test-Path -LiteralPath (Join-Path $options.CaddyDataPath 'pki/authorities/local/root.key')) '失败不能删除已生成的 CA。' }
    Write-Output "通过：$stage 阶段失败恢复。"
}
$options = New-HostingFixture 'success'
$script:FailureStage = ''
$state = Invoke-CaddyFirstInstallation @options
Assert-HostingTest ($script:Backend -and $script:Caddy -and $script:Firewall) '成功必须保留两项服务及 HTTPS 规则。'
Assert-HostingTest ($script:Events -notcontains 'StartSite') '成功不能重新启动旧 IIS。'
Assert-HostingTest (!$script:IisAutoStarts.serverAutoStart -and !$script:IisAutoStarts.autoStart) '成功必须阻止旧 IIS 在重启后抢占端口。'
Assert-HostingTest (Test-Path -LiteralPath (Join-Path $options.CaddyInstallPath '托管状态.json')) '成功须留下状态记录。'
Write-Output '通过：首次切换成功及状态记录。'
$script:Iis.SiteWasStarted = $false
$script:Iis.PoolWasStarted = $false
$options = New-HostingFixture 'originally-stopped'
$script:Iis.SiteWasStarted = $false; $script:Iis.PoolWasStarted = $false
$script:Iis.SiteAutoStart = $false; $script:Iis.PoolAutoStart = $false
$script:FailureStage = 'Backend'
try { Invoke-CaddyFirstInstallation @options | Out-Null } catch { }
Assert-HostingTest ($script:Events -notcontains 'StartSite' -and $script:Events -notcontains 'StartPool') '原本停止的站点不能因恢复被启动。'
Assert-HostingTest (!$script:IisAutoStarts.serverAutoStart -and !$script:IisAutoStarts.autoStart) '原本不自动启动的站点不能因恢复被启用。'

# 使用真实 IIS 归属检查及提供程序替身，验证不停止共享应用池或抢占其他 HTTPS 网站。
function Import-Module { param($Name,$ErrorAction); if ($Name -ne 'WebAdministration') { throw '测试意外模块。' } }
function Get-Website { param($Name); if ($Name) { return $script:FakeSites[0] }; return $script:FakeSites }
function Get-WebApplication { return $script:FakeApplications }
function Get-WebAppPoolState { param($Name); [PSCustomObject]@{ Value = 'Started' } }
function Get-ItemProperty { param($LiteralPath,$Name); [PSCustomObject]@{ autoStart = $true } }
$script:FakeSites = @([PSCustomObject]@{ Name = 'test-site'; applicationPool = 'test-pool'; State = 'Started'; serverAutoStart = $true; physicalPath = $script:Iis.PhysicalPath; bindings = [PSCustomObject]@{ Collection = @() } })
$script:FakeApplications = @()
$validIis = & $realIisPreflight 'test-site' '192.0.2.12'
Assert-HostingTest ($validIis.SiteWasStarted -and $validIis.PoolWasStarted) '独立 IIS 站点预检须正确记录原状态。'
foreach ($conflict in @('SharedPool','ChildApp','HttpsBinding')) {
    $script:FakeSites = @($script:FakeSites[0]); $script:FakeApplications = @()
    if ($conflict -eq 'SharedPool') { $script:FakeSites += [PSCustomObject]@{ Name = 'other'; applicationPool = 'test-pool'; State = 'Started' } }
    if ($conflict -eq 'ChildApp') { $script:FakeApplications = @([PSCustomObject]@{ applicationPool = 'test-pool' }) }
    if ($conflict -eq 'HttpsBinding') { $script:FakeSites += [PSCustomObject]@{ Name = 'other'; applicationPool = 'other-pool'; State = 'Started'; bindings = [PSCustomObject]@{ Collection = @([PSCustomObject]@{ protocol = 'https'; bindingInformation = '*:443:' }) } } }
    $threw = $false
    try { & $realIisPreflight 'test-site' '192.0.2.12' | Out-Null } catch { $threw = $true }
    Assert-HostingTest $threw "$conflict 必须阻止接管。"
}
Write-Output '通过：独立 IIS 归属、共享应用池/子应用保护、其他 HTTPS 站点冲突保护。'
Write-Output "首次托管替身验证完成：$workspace。未改变系统服务、IIS、防火墙、证书信任或数据库。"
