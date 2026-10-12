#Requires -Version 5.1
<#
.SYNOPSIS
创建或更新每日备份计划任务，供管理员或后续部署窗口调用。
.DESCRIPTION
Credential 为执行任务的 Windows 账号；未传入时交互获取，密码不写配置。
支持 WhatIf 预览，不在注册后自动执行备份。
#>
[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot '../本机配置/PostgreSQL/备份设置.json'),
    [PSCredential]$Credential
)
$ErrorActionPreference = 'Stop'
$ConfigPath = (Resolve-Path -LiteralPath $ConfigPath).ProviderPath
if ($ConfigPath -match '["\r\n]') { throw '配置路径不能包含引号或换行。' }
$scriptFile = Join-Path $PSScriptRoot '每日备份.ps1'
$settings = & $scriptFile -ConfigPath $ConfigPath -ValidateOnly
if (!$PSCmdlet.ShouldProcess($settings.TaskName, "注册每日 $($settings.DailyAt) 的备份任务，配置：$ConfigPath")) { return }
if (!$Credential) {
    $Credential = Get-Credential -UserName ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -Message '请输入执行备份任务的 Windows 账号及登录密码'
}
if (!$Credential) { throw '已取消创建任务。' }
$action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
    -Argument ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{0}" -ConfigPath "{1}"' -f $scriptFile, $ConfigPath) `
    -WorkingDirectory $PSScriptRoot
$nextRun = (Get-Date).Date.Add([TimeSpan]::ParseExact($settings.DailyAt, 'hh\:mm', [Globalization.CultureInfo]::InvariantCulture))
if ($nextRun -le (Get-Date)) { $nextRun = $nextRun.AddDays(1) }
$trigger = New-ScheduledTaskTrigger -Daily -At $nextRun
$taskSettings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
# Password 登录方式允许用户未登录时运行；Windows 密码只交给任务计划程序保存。
Register-ScheduledTask -TaskName $settings.TaskName -TaskPath '\' -Action $action -Trigger $trigger -Settings $taskSettings `
    -User $Credential.UserName -Password $Credential.GetNetworkCredential().Password -RunLevel Limited `
    -Description "每日备份 PostgreSQL $($settings.DatabaseName) 到 $($settings.BackupDirectory)" -Force | Out-Null
Get-ScheduledTask -TaskName $settings.TaskName -TaskPath '\'
