[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$server = '(localdb)\MSSQLLocalDB'
$prefix = 'SwiftExportNetDemandTest_'
$suffix = '{0}_{1}' -f (Get-Date -Format 'yyyyMMddHHmmssfff'), $PID
$database = $prefix + $suffix
$restoreDatabase = $database + '_Restore'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$migrationRoot = Join-Path $repositoryRoot 'SwiftExport.Infrastructure\Migrations'
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$workRoot = [System.IO.Path]::GetFullPath((Join-Path $tempRoot $database))

if ((-not $database.StartsWith($prefix, [System.StringComparison]::Ordinal)) -or
    (-not $restoreDatabase.StartsWith($prefix, [System.StringComparison]::Ordinal))) {
    throw 'Generated database names failed the SwiftExportNetDemandTest_ safety check.'
}
if (-not $workRoot.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Temporary work path escaped the system temp directory: $workRoot"
}

$sqlcmd = (Get-Command sqlcmd.exe -ErrorAction Stop).Source
$fixture = Join-Path $PSScriptRoot 'net_demand_localdb_fixture_before_006.sql'
$preflight = Join-Path $PSScriptRoot 'net_demand_workspace_preflight.sql'
$migration006 = Join-Path $migrationRoot '006_NetDemandPlanning_AdditiveSchema.sql'
$migration007 = Join-Path $migrationRoot '007_NetDemandPlanning_BackfillAndConstraints.sql'
$postflight = Join-Path $PSScriptRoot 'net_demand_workspace_postflight.sql'
$capture = Join-Path $PSScriptRoot 'net_demand_localdb_capture_first_pass.sql'
$assertions = Join-Path $PSScriptRoot 'net_demand_localdb_assert_second_pass.sql'
$requiredFiles = @($fixture,$preflight,$migration006,$migration007,$postflight,$capture,$assertions)
foreach ($path in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required gate script is missing: $path"
    }
}

New-Item -ItemType Directory -Path $workRoot | Out-Null
$databaseMdf = Join-Path $workRoot ($database + '.mdf')
$databaseLdf = Join-Path $workRoot ($database + '_log.ldf')
$restoreMdf = Join-Path $workRoot ($restoreDatabase + '.mdf')
$restoreLdf = Join-Path $workRoot ($restoreDatabase + '_log.ldf')
$backup = Join-Path $workRoot ($database + '.bak')
$logicalData = $database + '_data'
$logicalLog = $database + '_log'

function Escape-SqlLiteral([string]$value) {
    return $value.Replace("'", "''")
}

function Invoke-SqlQuery([string]$targetDatabase, [string]$query, [string]$label) {
    Write-Host "==> $label"
    & $sqlcmd -S $server -E -C -d $targetDatabase -b -r 1 -I -Q $query
    if ($LASTEXITCODE -ne 0) {
        throw "$label failed with sqlcmd exit code $LASTEXITCODE."
    }
}

function Invoke-SqlFile([string]$targetDatabase, [string]$path, [string]$label) {
    Write-Host "==> $label"
    & $sqlcmd -S $server -E -C -d $targetDatabase -b -r 1 -I -f 65001 -i $path
    if ($LASTEXITCODE -ne 0) {
        throw "$label failed with sqlcmd exit code $LASTEXITCODE."
    }
}

function Drop-TestDatabase([string]$name) {
    if (-not $name.StartsWith($prefix, [System.StringComparison]::Ordinal)) {
        throw "Refusing to drop database without required prefix: $name"
    }
    $query = @"
IF DB_ID(N'$(Escape-SqlLiteral $name)') IS NOT NULL
BEGIN
    ALTER DATABASE [$name] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$name];
END;
"@
    & $sqlcmd -S $server -E -C -d master -b -r 1 -I -Q $query
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to drop disposable database $name."
    }
}

$gateSucceeded = $false
$cleanupFailures = [System.Collections.Generic.List[string]]::new()
try {
    $createQuery = @"
IF DB_ID(N'$(Escape-SqlLiteral $database)') IS NOT NULL
    THROW 51900,N'Disposable database name already exists.',1;
CREATE DATABASE [$database]
ON PRIMARY
(
    NAME=N'$(Escape-SqlLiteral $logicalData)',
    FILENAME=N'$(Escape-SqlLiteral $databaseMdf)'
)
LOG ON
(
    NAME=N'$(Escape-SqlLiteral $logicalLog)',
    FILENAME=N'$(Escape-SqlLiteral $databaseLdf)'
);
"@
    Invoke-SqlQuery 'master' $createQuery "Create disposable LocalDB database $database"

    Invoke-SqlFile $database $fixture 'Create minimal pre-006 fixture'
    Invoke-SqlFile $database $preflight 'Run read-only preflight'

    Invoke-SqlFile $database $migration006 'Run 006 first pass'
    Invoke-SqlFile $database $migration007 'Run 007 first pass'
    Invoke-SqlFile $database $postflight 'Run postflight after first pass'
    Invoke-SqlFile $database $capture 'Capture first-pass business and audit rows'

    Invoke-SqlFile $database $migration006 'Run 006 second pass'
    Invoke-SqlFile $database $migration007 'Run 007 second pass'
    Invoke-SqlFile $database $postflight 'Run postflight after second pass'
    Invoke-SqlFile $database $assertions 'Assert second-pass idempotency and schema'

    $backupQuery = @"
BACKUP DATABASE [$database]
TO DISK=N'$(Escape-SqlLiteral $backup)'
WITH INIT,CHECKSUM,STATS=10;
RESTORE VERIFYONLY
FROM DISK=N'$(Escape-SqlLiteral $backup)'
WITH CHECKSUM;
"@
    Invoke-SqlQuery 'master' $backupQuery 'Create and verify disposable .bak backup'

    $restoreQuery = @"
IF DB_ID(N'$(Escape-SqlLiteral $restoreDatabase)') IS NOT NULL
    THROW 51901,N'Disposable restore database name already exists.',1;
RESTORE DATABASE [$restoreDatabase]
FROM DISK=N'$(Escape-SqlLiteral $backup)'
WITH
    MOVE N'$(Escape-SqlLiteral $logicalData)' TO N'$(Escape-SqlLiteral $restoreMdf)',
    MOVE N'$(Escape-SqlLiteral $logicalLog)' TO N'$(Escape-SqlLiteral $restoreLdf)',
    RECOVERY,REPLACE,STATS=10;
"@
    Invoke-SqlQuery 'master' $restoreQuery "Restore backup as $restoreDatabase"
    Invoke-SqlFile $restoreDatabase $postflight 'Run postflight against restored database'
    Invoke-SqlFile $restoreDatabase $assertions 'Assert restored database'

    Write-Host "NET_DEMAND_LOCALDB_GATE_PASSED primary=$database restore=$restoreDatabase"
    $gateSucceeded = $true
}
finally {
    foreach ($name in @($restoreDatabase,$database)) {
        try {
            Drop-TestDatabase $name
        }
        catch {
            $cleanupFailures.Add($_.Exception.Message)
        }
    }
    try {
        if (Test-Path -LiteralPath $workRoot) {
            $verifiedWorkRoot = [System.IO.Path]::GetFullPath($workRoot)
            if ((-not $verifiedWorkRoot.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) -or
                (-not ([System.IO.Path]::GetFileName($verifiedWorkRoot)).StartsWith($prefix, [System.StringComparison]::Ordinal))) {
                throw "Refusing to remove unverified temporary path: $verifiedWorkRoot"
            }
            Remove-Item -LiteralPath $verifiedWorkRoot -Recurse -Force
        }
    }
    catch {
        $cleanupFailures.Add($_.Exception.Message)
    }
    if (-not $gateSucceeded) {
        foreach ($failure in $cleanupFailures) {
            Write-Warning "LocalDB gate cleanup also failed: $failure"
        }
    }
}

if ($cleanupFailures.Count -gt 0) {
    throw ('LocalDB gate passed but cleanup failed: ' + ($cleanupFailures -join ' | '))
}
