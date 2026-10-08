#Requires -Version 5.1
#Requires -RunAsAdministrator
<#! 在服务器删除本项目旧 IIS 站点和独立应用池；保留程序、生产配置、文件存储、证书和数据库。 !#>
[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [string]$SiteName = 'SwiftExport_WebApi',
    [string]$ExpectedPhysicalPath = 'D:\Apps\SwiftExport_WebApi'
)
$ErrorActionPreference = 'Stop'
Import-Module WebAdministration -ErrorAction Stop
$sites = @(Get-Website | Where-Object { $_.Name -eq $SiteName })
if ($sites.Count -ne 1) { throw '未找到唯一的指定站点，停止清理，请核对服务器 IIS 网站列表。' }
$site = $sites[0]
$actual = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables([string]$site.physicalPath)).TrimEnd('\')
$expected = [IO.Path]::GetFullPath($ExpectedPhysicalPath).TrimEnd('\')
if (![string]::Equals($actual,$expected,[StringComparison]::OrdinalIgnoreCase)) { throw '站点物理路径与旧部署记录不符，停止清理；核对后通过参数指定真实路径。' }
$pool = [string]$site.applicationPool
if ([string]::IsNullOrWhiteSpace($pool)) { throw '站点没有明确应用池，停止清理。' }
# 避免停止共享身份下的其他程序；子应用也要求先单独核对，不按名称猜测归属。
$otherSites = @(Get-Website | Where-Object { $_.Name -ne $SiteName -and $_.applicationPool -eq $pool })
$applications = @(Get-WebApplication | Where-Object { $_.applicationPool -eq $pool })
if ($otherSites.Count -gt 0 -or $applications.Count -gt 0) { throw '应用池仍被其他网站或子应用使用，停止清理；请先核对使用者。' }
Write-Output "清理范围：IIS 网站 $SiteName；应用池 $pool；物理目录 $actual 保留。"
if ($PSCmdlet.ShouldProcess("$SiteName / $pool",'备份 IIS 配置，再停止并删除旧站点和应用池')) {
    # 备份只留在服务器 IIS 配置备份目录；不复制配置或证书给客户端。
    $backupName = 'SwiftExport_BeforeRemoval_' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '_' + [Guid]::NewGuid().ToString('N').Substring(0,8)
    Backup-WebConfiguration -Name $backupName -ErrorAction Stop | Out-Null
    Write-Output "IIS 配置备份：$backupName"
    if ($site.State -eq 'Started') { Stop-Website -Name $SiteName -ErrorAction Stop }
    if ((Get-WebAppPoolState -Name $pool).Value -ne 'Stopped') { Stop-WebAppPool -Name $pool -ErrorAction Stop }
    Remove-Website -Name $SiteName -ErrorAction Stop
    Remove-WebAppPool -Name $pool -ErrorAction Stop
    Write-Output '旧 IIS 站点和应用池已删除。新安装勾选“不迁移 IIS”。现有 .NET、PG、程序目录、图片和证书均未删除。'
}
