#Requires -Version 5.1
#Requires -RunAsAdministrator
<#!
在 Windows Server 的管理员 Windows PowerShell 中安装/升级专用 IIS 站点。
复用证书和生产配置，不操作数据库，不清理文件根目录。
!#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PackagePath,
    [Parameter(Mandatory=$true)][System.Net.IPAddress]$ServerIp,
    [Parameter(Mandatory=$true)][string]$CertificateThumbprint,
    [string]$ConfigurationPath,
    [string]$InstallPath = 'D:\Apps\SwiftExport.WebApi',
    [ValidateRange(1,65535)][int]$Port = 443,
    [string[]]$InternalSubnets = @('LocalSubnet')
)
$ErrorActionPreference = 'Stop'
if ($ServerIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) { throw '首批部署脚本使用固定内网 IPv4 地址，不自动开放 IPv6 入口。' }
Import-Module WebAdministration
$site = 'SwiftExport'
$package = (Resolve-Path -LiteralPath $PackagePath).Path.TrimEnd('\')
$target = [IO.Path]::GetFullPath($InstallPath).TrimEnd('\')
if (![IO.Path]::IsPathRooted($InstallPath) -or $target -eq [IO.Path]::GetPathRoot($target).TrimEnd('\')) { throw '安装目录必须为独立绝对子目录。' }
if ($package -eq $target -or $package.StartsWith($target + '\',[StringComparison]::OrdinalIgnoreCase) -or $target.StartsWith($package + '\',[StringComparison]::OrdinalIgnoreCase)) { throw '发布包和安装目录不能相同或互相包含。' }
foreach ($file in @('SwiftExport.WebApi.dll','web.config')) {
    if (!(Test-Path -LiteralPath (Join-Path $package $file))) { throw "发布包缺少 $file，请使用 dotnet publish。" }
}
if (!(Get-WebGlobalModule | Where-Object Name -eq 'AspNetCoreModuleV2')) { throw '请先安装 .NET 10 Hosting Bundle 并重启 IIS。' }
$cert = Get-Item -LiteralPath ('Cert:\LocalMachine\My\' + $CertificateThumbprint.Replace(' ',''))
if (!$cert.HasPrivateKey -or $cert.NotAfter -le (Get-Date)) { throw 'HTTPS 证书无私钥或已经过期。' }
$configTarget = Join-Path $target 'appsettings.Production.json'
$exists = Test-Path -LiteralPath $configTarget
if ($exists -and $ConfigurationPath) { throw '升级保留原配置；如需改配置，请管理员单独修改，升级时不传 ConfigurationPath。' }
if (!$exists -and !$ConfigurationPath) { throw '首次部署请提供填写完成的服务器配置 JSON。' }
$configSource = if ($exists) { $configTarget } else { (Resolve-Path -LiteralPath $ConfigurationPath).Path }
$config = Get-Content -LiteralPath $configSource -Raw | ConvertFrom-Json
if ($config.Auth.UseDevUser -eq $true -or [string]::IsNullOrWhiteSpace($config.ConnectionStrings.Default) -or $config.ConnectionStrings.Default -match '填写' -or ([string]$config.Auth.JwtKey).Length -lt 32) { throw '请配置真实数据库连接、至少32字符的随机 JWT 密钥，并关闭 UseDevUser。' }
if ([string]::IsNullOrWhiteSpace($config.Auth.JwtIssuer) -or [string]::IsNullOrWhiteSpace($config.Auth.JwtAudience)) { throw 'JWT Issuer 和 Audience 不能为空。' }
$storage = [string]$config.FileStorage.RootPath
if (!$config.FileStorage.Enabled -or ![IO.Path]::IsPathRooted($storage)) { throw '请启用文件存储并配置绝对根目录。' }
$storage = [IO.Path]::GetFullPath($storage).TrimEnd('\')
if ($storage -eq [IO.Path]::GetPathRoot($storage).TrimEnd('\') -or $storage -eq $target -or $storage.StartsWith($target + '\',[StringComparison]::OrdinalIgnoreCase) -or $target.StartsWith($storage + '\',[StringComparison]::OrdinalIgnoreCase)) { throw '文件根目录与程序目录必须独立，不得使用磁盘根目录。' }
$binding = $ServerIp.ToString() + ':' + $Port + ':'
$existingSite = Get-Website -Name $site -ErrorAction SilentlyContinue
if (!$existingSite -and (Test-Path -LiteralPath $target) -and (Get-ChildItem -LiteralPath $target -Force | Select-Object -First 1)) { throw '首次安装目标目录非空，请选择独立空目录，不能覆盖不明来源的部署。' }
if ($existingSite -and ([IO.Path]::GetFullPath($existingSite.physicalPath).TrimEnd('\') -ne $target -or $existingSite.applicationPool -ne $site)) { throw '同名 IIS 站点不是目标专用站点，停止部署。' }
foreach ($other in Get-Website | Where-Object Name -ne $site) {
    if ($other.applicationPool -eq $site) { throw 'SwiftExport 应用池被其他站点使用，不能作为专用部署目标。' }
    if ($other.bindings.Collection | Where-Object { $_.bindingInformation -eq $binding -or $_.bindingInformation -eq ('*:' + $Port + ':') }) { throw '该端口绑定与其他站点冲突，请选用独立端口。' }
}
New-Item -ItemType Directory -Path $target -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $storage 'ProductImages') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $target 'Logs') -Force | Out-Null
if (!(Test-Path "IIS:\AppPools\$site")) { New-WebAppPool -Name $site | Out-Null }
Set-ItemProperty "IIS:\AppPools\$site" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$site" -Name processModel.identityType -Value 4
Set-ItemProperty "IIS:\AppPools\$site" -Name enable32BitAppOnWin64 -Value $false

# 更新前下线本站点；复制失败保留 offline，防止运行半更新程序。不使用镜像删除。
$offline = Join-Path $target 'app_offline.htm'
Set-Content -LiteralPath $offline -Value 'SwiftExport 正在维护，请稍后重试。' -Encoding UTF8
if ($existingSite) { Stop-Website -Name $site }
if ((Get-WebAppPoolState -Name $site).Value -ne 'Stopped') { Stop-WebAppPool -Name $site }
& robocopy $package $target /E /R:2 /W:1 /XF 'appsettings*.json' 'app_offline.htm' /XD Logs /NFL /NDL /NJH /NJS
if ($LASTEXITCODE -ge 8) { throw '发布文件复制失败，已保持维护状态。' }
if (!$exists) {
    Set-Content -LiteralPath (Join-Path $target 'appsettings.json') -Value '{}' -Encoding UTF8
    Copy-Item -LiteralPath $configSource -Destination $configTarget
}
# 程序只读，日志可写；首期图片只读，不给应用池删除或覆盖图片权限。
& icacls $target /grant ('IIS AppPool\' + $site + ':(OI)(CI)RX') | Out-Null
if ($LASTEXITCODE -ne 0) { throw '程序权限设置失败。' }
& icacls (Join-Path $target 'Logs') /grant ('IIS AppPool\' + $site + ':(OI)(CI)M') | Out-Null
if ($LASTEXITCODE -ne 0) { throw '日志权限设置失败。' }
& icacls $storage /grant ('IIS AppPool\' + $site + ':(OI)(CI)RX') | Out-Null
if ($LASTEXITCODE -ne 0) { throw '图片目录权限设置失败。' }
# 固定 Production，避免继承开发身份配置。
[xml]$webConfig = Get-Content -LiteralPath (Join-Path $target 'web.config') -Raw
$asp = $webConfig.SelectSingleNode('//aspNetCore')
if (!$asp) { throw 'web.config 缺少 aspNetCore 节点。' }
$envs = $asp.SelectSingleNode('environmentVariables')
if (!$envs) { $envs = $webConfig.CreateElement('environmentVariables'); [void]$asp.AppendChild($envs) }
foreach ($name in @('ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT')) {
    $node = $envs.SelectSingleNode("environmentVariable[@name='$name']")
    if (!$node) { $node = $webConfig.CreateElement('environmentVariable'); $node.SetAttribute('name',$name); [void]$envs.AppendChild($node) }
    $node.SetAttribute('value','Production')
}
$webConfig.Save((Join-Path $target 'web.config'))
if (!$existingSite) { New-Website -Name $site -PhysicalPath $target -ApplicationPool $site -IPAddress $ServerIp.ToString() -Port $Port -Ssl | Out-Null }
$https = Get-WebBinding -Name $site -Protocol https | Where-Object bindingInformation -eq $binding
if (!$https) { throw '现有站点地址与参数不一致；请明确迁移绑定，不自动增加第二个访问入口。' }
$https.AddSslCertificate($cert.Thumbprint,'My')
$rule = 'SwiftExport-Internal-HTTPS'
if (Get-NetFirewallRule -Name $rule -ErrorAction SilentlyContinue) {
    Set-NetFirewallRule -Name $rule -Enabled True -Profile Domain,Private -Action Allow
    Get-NetFirewallRule -Name $rule | Get-NetFirewallPortFilter | Set-NetFirewallPortFilter -Protocol TCP -LocalPort $Port
    Get-NetFirewallRule -Name $rule | Get-NetFirewallAddressFilter | Set-NetFirewallAddressFilter -LocalAddress $ServerIp.ToString() -RemoteAddress $InternalSubnets
} else {
    New-NetFirewallRule -Name $rule -DisplayName 'SwiftExport 内网 HTTPS' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -LocalAddress $ServerIp.ToString() -RemoteAddress $InternalSubnets -Profile Domain,Private | Out-Null
}
Remove-Item -LiteralPath $offline
if ((Get-WebAppPoolState -Name $site).Value -ne 'Started') { Start-WebAppPool -Name $site }
Start-Website -Name $site
Write-Output "部署命令已执行。请在已信任内部根证书的客户端验证 https://${ServerIp}:$Port/files/ProductImages/实际图片.jpg 。未执行数据库迁移，未验证业务登录。"
