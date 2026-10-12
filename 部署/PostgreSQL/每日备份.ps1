#Requires -Version 5.1
<#
.SYNOPSIS
备份业务库和全局角色，检查成功后发布到 NAS 同步目录。
.DESCRIPTION
读取不含密码的 JSON 设置及服务器 pgpass.conf；返回进程退出码 0/1。
ValidateOnly 只检查本地前置条件并返回设置对象，不连接数据库、不创建目录。
#>
[CmdletBinding()]
param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot '../本机配置/PostgreSQL/备份设置.json'),
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
$logFile = $null
$lockStream = $null
$oldPassFile = $env:PGPASSFILE
$oldConnectTimeout = $env:PGCONNECT_TIMEOUT
$oldPassword = $env:PGPASSWORD

# 用独立输出文件收集原生工具错误，只有退出码为零才允许进入下一阶段。
function Invoke-BackupTool {
    param([string]$Name, [string]$ArgumentLine, [string]$Step)
    $outFile = Join-Path $runDirectory "$Step.out.log"
    $errFile = Join-Path $runDirectory "$Step.err.log"
    Add-Content -LiteralPath $logFile -Encoding UTF8 -Value "开始：$Step"
    $process = Start-Process -FilePath (Join-Path $settings.PgBin $Name) `
        -ArgumentList $ArgumentLine -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $outFile -RedirectStandardError $errFile
    if ((Get-Item -LiteralPath $errFile).Length -gt 0) {
        Get-Content -LiteralPath $errFile -Encoding UTF8 | Add-Content -LiteralPath $logFile -Encoding UTF8
    }
    if ($process.ExitCode -ne 0) { throw "$Step 失败，退出码：$($process.ExitCode)" }
}

try {
    $settings = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($key in @('PgBin', 'HostName', 'UserName', 'DatabaseName', 'BackupDirectory', 'WorkDirectory', 'PasswordFile', 'TaskName', 'DailyAt')) {
        $value = [string]$settings.$key
        if ([string]::IsNullOrWhiteSpace($value) -or $value -match '["\r\n]') { throw "设置 $key 为空或包含不支持的引号、换行。" }
    }
    if ($settings.Port -notmatch '^\d+$' -or [int]$settings.Port -lt 1 -or [int]$settings.Port -gt 65535) { throw 'Port 必须是 1 到 65535。' }
    if ($settings.DailyAt -notmatch '^([01]\d|2[0-3]):[0-5]\d$') { throw 'DailyAt 必须为 HH:mm，例如 02:00。' }
    if ($settings.TaskName -match '[\\/:*?<>|]') { throw 'TaskName 不能包含路径或文件名特殊字符。' }
    if ($settings.DatabaseName -match '[\\/:*?<>|]' -or $settings.DatabaseName.EndsWith('\')) { throw 'DatabaseName 必须可用于备份文件名。' }
    # 使用本机绝对路径；同盘移动避免 NAS 读到正在跨盘复制的归档。
    foreach ($key in @('PgBin', 'BackupDirectory', 'WorkDirectory', 'PasswordFile')) {
        if ($settings.$key -notmatch '^[A-Za-z]:[\\/]') { throw "$key 必须为本机盘符绝对路径。" }
        $settings.$key = [IO.Path]::GetFullPath($settings.$key)
    }
    $backupRoot = $settings.BackupDirectory.TrimEnd('\', '/') + '\'
    $workRoot = $settings.WorkDirectory.TrimEnd('\', '/') + '\'
    if ([IO.Path]::GetPathRoot($backupRoot) -ne [IO.Path]::GetPathRoot($workRoot)) { throw 'WorkDirectory 与 BackupDirectory 必须位于同一盘符。' }
    if ($workRoot.StartsWith($backupRoot, [StringComparison]::OrdinalIgnoreCase) -or $backupRoot.StartsWith($workRoot, [StringComparison]::OrdinalIgnoreCase)) { throw '工作目录和备份目录不能相同或互相包含。' }
    if ($settings.PasswordFile.StartsWith($backupRoot, [StringComparison]::OrdinalIgnoreCase)) { throw '密码文件不能放入 NAS 备份目录。' }
    foreach ($name in @('pg_dump.exe', 'pg_dumpall.exe', 'pg_restore.exe')) {
        if (!(Test-Path -LiteralPath (Join-Path $settings.PgBin $name) -PathType Leaf)) { throw "找不到 PostgreSQL 工具：$name" }
    }
    $passBytes = [IO.File]::ReadAllBytes($settings.PasswordFile)
    if ($passBytes.Length -eq 0) { throw 'pgpass.conf 为空。' }
    # pgpass 首行不能带 BOM；严格 UTF-8 解码只校验编码，不输出凭据内容。
    if (($passBytes.Length -ge 3 -and $passBytes[0] -eq 239 -and $passBytes[1] -eq 187 -and $passBytes[2] -eq 191) -or
        ($passBytes.Length -ge 2 -and (($passBytes[0] -eq 255 -and $passBytes[1] -eq 254) -or ($passBytes[0] -eq 254 -and $passBytes[1] -eq 255)))) { throw 'pgpass.conf 必须保存为 UTF-8 无 BOM。' }
    $null = [Text.UTF8Encoding]::new($false, $true).GetString($passBytes)
    if ($ValidateOnly) { return $settings }

    [void][IO.Directory]::CreateDirectory($settings.WorkDirectory)
    # 文件排他锁覆盖手动运行与计划任务，防止同一配置的备份重入。
    $lockStream = [IO.File]::Open((Join-Path $settings.WorkDirectory 'backup.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $runDirectory = Join-Path $settings.WorkDirectory ($stamp + '_' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($runDirectory)
    [void][IO.Directory]::CreateDirectory($settings.BackupDirectory)
    $backupName = "$($settings.DatabaseName)_pg_$stamp.backup"
    $globalsName = "pg_globals_$stamp.sql"
    $logFile = Join-Path $settings.BackupDirectory "$($settings.DatabaseName)_pg_$stamp.log"
    Add-Content -LiteralPath $logFile -Encoding UTF8 -Value "开始备份：$(Get-Date -Format s)"
    $env:PGPASSFILE = $settings.PasswordFile
    $env:PGCONNECT_TIMEOUT = '15'
    $env:PGPASSWORD = $null
    $connection = '-h "{0}" -p {1} -U "{2}" -w' -f $settings.HostName, $settings.Port, $settings.UserName
    $dbStaged = Join-Path $runDirectory $backupName
    $globalsStaged = Join-Path $runDirectory $globalsName
    Invoke-BackupTool 'pg_dump.exe' ('{0} -d "{1}" -Fc -f "{2}"' -f $connection, $settings.DatabaseName, $dbStaged) 'database'
    Invoke-BackupTool 'pg_dumpall.exe' ('{0} -l "{1}" --globals-only -f "{2}"' -f $connection, $settings.DatabaseName, $globalsStaged) 'globals'
    foreach ($file in @($dbStaged, $globalsStaged)) {
        if ((Get-Item -LiteralPath $file).Length -eq 0) { throw '备份工具生成了空文件。' }
    }
    # 目录可读检查不能替代实际恢复演练；失败时归档留在工作目录。
    Invoke-BackupTool 'pg_restore.exe' ('--list "{0}"' -f $dbStaged) 'archive-check'
    Move-Item -LiteralPath $globalsStaged -Destination (Join-Path $settings.BackupDirectory $globalsName)
    Move-Item -LiteralPath $dbStaged -Destination (Join-Path $settings.BackupDirectory $backupName)
    Add-Content -LiteralPath $logFile -Encoding UTF8 -Value "SUCCESS：$(Get-Date -Format s)；$backupName；$globalsName"
    Write-Output "备份完成：$backupName；日志：$logFile"
}
catch {
    if ($ValidateOnly) { throw }
    if ($logFile) { Add-Content -LiteralPath $logFile -Encoding UTF8 -Value "FAILED：$($_.Exception.Message)" -ErrorAction SilentlyContinue }
    Write-Error "备份失败：$($_.Exception.Message)" -ErrorAction Continue
    exit 1
}
finally {
    if ($lockStream) { $lockStream.Dispose() }
    $env:PGPASSFILE = $oldPassFile
    $env:PGCONNECT_TIMEOUT = $oldConnectTimeout
    $env:PGPASSWORD = $oldPassword
}
if (!$ValidateOnly) { exit 0 }
