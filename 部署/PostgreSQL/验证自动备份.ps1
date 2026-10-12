#Requires -Version 5.1
<# 验证真实子进程退出码、发布边界及任务注册参数；只使用隔离工具替身，不连接数据库或注册任务。 #>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$workspace = Join-Path $repository ('artifacts/部署/验证/PG备份 中文 空格/' + [Guid]::NewGuid().ToString('N'))
$fakeBin = Join-Path $workspace '工具'
[void][IO.Directory]::CreateDirectory($fakeBin)
$shell = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$oldMode = $env:SWIFT_BACKUP_TEST_MODE

# 替身以真实 EXE 形式解析参数并模拟失败，使含中文、空格路径也经过进程边界。
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
public static class FakePgTool {
    public static int Main(string[] args) {
        string tool = Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);
        string mode = Environment.GetEnvironmentVariable("SWIFT_BACKUP_TEST_MODE");
        if (mode == tool) { Console.Error.WriteLine("模拟工具失败"); return 9; }
        if (tool == "pg_restore") return args.Length == 2 && args[0] == "--list" && File.Exists(args[1]) ? 0 : 8;
        if (Array.IndexOf(args, "-w") < 0 || String.IsNullOrEmpty(Environment.GetEnvironmentVariable("PGPASSFILE"))) return 7;
        if (tool == "pg_dumpall" && Array.IndexOf(args, "-l") < 0) return 6;
        int index = Array.IndexOf(args, "-f");
        if (index < 0 || index + 1 >= args.Length) return 5;
        File.WriteAllText(args[index + 1], mode == "empty" ? "" : "isolated test data");
        return 0;
    }
}
'@ -OutputAssembly (Join-Path $fakeBin 'pg_dump.exe') -OutputType ConsoleApplication
Copy-Item -LiteralPath (Join-Path $fakeBin 'pg_dump.exe') -Destination (Join-Path $fakeBin 'pg_dumpall.exe')
Copy-Item -LiteralPath (Join-Path $fakeBin 'pg_dump.exe') -Destination (Join-Path $fakeBin 'pg_restore.exe')

# 替代计划任务写入口，验证传参但绝不接触操作系统任务存储。
$registrationCapture = @{ Value = $null }
function New-ScheduledTaskAction { param($Execute, $Argument, $WorkingDirectory) return $PSBoundParameters }
function New-ScheduledTaskTrigger { param([switch]$Daily, $At) return $PSBoundParameters }
function New-ScheduledTaskSettingsSet { param([switch]$StartWhenAvailable, $MultipleInstances, [switch]$AllowStartIfOnBatteries, [switch]$DontStopIfGoingOnBatteries) return $PSBoundParameters }
function Register-ScheduledTask {
    param($TaskName, $TaskPath, $Action, $Trigger, $Settings, $User, $Password, $RunLevel, $Description, [switch]$Force)
    $registrationCapture.Value = $PSBoundParameters
}
function Get-ScheduledTask { param($TaskName, $TaskPath) return [PSCustomObject]@{ TaskName = $TaskName; TaskPath = $TaskPath } }

try {
    foreach ($mode in @('success', 'pg_dump', 'pg_dumpall', 'pg_restore', 'empty', 'bom')) {
        $caseRoot = Join-Path $workspace $mode
        [void][IO.Directory]::CreateDirectory($caseRoot)
        $settings = Get-Content -LiteralPath (Join-Path $PSScriptRoot '备份设置示例.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $settings.PgBin = $fakeBin
        $settings.BackupDirectory = Join-Path $caseRoot '备份'
        $settings.WorkDirectory = Join-Path $caseRoot '工作'
        $settings.PasswordFile = Join-Path $caseRoot 'pgpass.conf'
        [IO.File]::WriteAllText($settings.PasswordFile, '127.0.0.1:5432:dv_next:postgres:fake-password', [Text.UTF8Encoding]::new($mode -eq 'bom'))
        $configPath = Join-Path $caseRoot '备份设置.json'
        $settings | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding UTF8
        $env:SWIFT_BACKUP_TEST_MODE = $mode
        $arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{0}" -ConfigPath "{1}"' -f (Join-Path $PSScriptRoot '每日备份.ps1'), $configPath
        $process = Start-Process -FilePath $shell -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput (Join-Path $caseRoot 'stdout.log') -RedirectStandardError (Join-Path $caseRoot 'stderr.log')
        $code = $process.ExitCode
        $published = @(Get-ChildItem -LiteralPath $settings.BackupDirectory -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in @('.backup', '.sql') })
        if ($mode -eq 'success') {
            if ($code -ne 0 -or $published.Count -ne 2) { throw "成功场景失败，见 $caseRoot 下日志。" }
            $log = Get-Content -LiteralPath (Get-ChildItem -LiteralPath $settings.BackupDirectory -Filter '*.log').FullName -Raw -Encoding UTF8
            if ($log -notmatch 'SUCCESS') { throw '成功场景缺少成功日志。' }
            $successConfig = $configPath
        } else {
            if ($code -ne 1 -or $published.Count -ne 0) { throw "失败场景 $mode 退出码或发布边界不正确。" }
        }
        Write-Output "通过：$mode"
    }
    & (Join-Path $PSScriptRoot '注册每日备份任务.ps1') -ConfigPath $successConfig -WhatIf
    if ($registrationCapture.Value) { throw 'WhatIf 不应注册任务。' }
    $credential = [PSCredential]::new('TEST\Backup', (ConvertTo-SecureString 'fake-windows-password' -AsPlainText -Force))
    $null = & (Join-Path $PSScriptRoot '注册每日备份任务.ps1') -ConfigPath $successConfig -Credential $credential
    $registered = $registrationCapture.Value
    if (!$registered -or $registered.RunLevel -ne 'Limited' -or $registered.Settings.MultipleInstances -ne 'IgnoreNew' -or
        !$registered.Settings.StartWhenAvailable -or !$registered.Trigger.Daily -or
        $registered.Trigger.At -le (Get-Date) -or $registered.Trigger.At.Hour -ne 2 -or
        !$registered.Action.Argument.Contains('-ConfigPath "' + $successConfig + '"') -or
        $registered.Action.Argument.Contains('fake-windows-password')) { throw '计划任务参数不符合约定。' }
    Write-Output "通过：任务预览、注册参数；隔离目录：$workspace"
} finally { $env:SWIFT_BACKUP_TEST_MODE = $oldMode }
