# 主控阶段回归：关闭所有数据库门禁，排除真实数据库用例；不构建、不连接数据库。
$ErrorActionPreference = 'Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskCategories = @(rg -o --no-filename 'TestCategory\("[^"]+"' SwiftExport.Application.Tests | ForEach-Object { $_ -replace '^TestCategory\("','' -replace '"$','' } | Where-Object { $_ -match 'Integration$' } | Sort-Object -Unique)
$taskGates = @(rg -o --no-filename 'GetEnvironmentVariable\("SWIFTEXPORT_RUN_[^"]+"' SwiftExport.Application.Tests | ForEach-Object { $_ -replace '^GetEnvironmentVariable\("','' -replace '"$','' } | Sort-Object -Unique)
if ($taskCategories.Count -ne 37 -or $taskGates.Count -ne 14) { throw '发现的数据库门禁或分类已变化，需重新检查范围。' }
$taskFilter = (@($taskCategories | ForEach-Object { 'TestCategory!=' + $_ }) + @('FullyQualifiedName!~RealPg_', 'FullyQualifiedName!~SqlServer', 'FullyQualifiedName!~LocalDbTests')) -join '&'
$taskOriginalGates = @{}
$taskExit = 1
try {
    foreach ($taskGate in $taskGates) {
        $taskOriginalGates[$taskGate] = [Environment]::GetEnvironmentVariable($taskGate, 'Process')
        [Environment]::SetEnvironmentVariable($taskGate, '0', 'Process')
    }
    dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter $taskFilter --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p08-backend-offline-stage.trx' -v minimal
    $taskExit = $LASTEXITCODE
}
finally {
    foreach ($taskGate in $taskGates) { [Environment]::SetEnvironmentVariable($taskGate, $taskOriginalGates[$taskGate], 'Process') }
}
exit $taskExit

