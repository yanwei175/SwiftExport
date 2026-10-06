# P09-R03/T01：原密码恢复行为及两个字典入口定向离线；不执行工具、不连接数据库。
$ErrorActionPreference = 'Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskCategories = @(rg -o --no-filename 'TestCategory\("[^"]+"' SwiftExport.Application.Tests | ForEach-Object { $_ -replace '^TestCategory\("','' -replace '"$','' } | Where-Object { $_ -match 'Integration$' } | Sort-Object -Unique)
$taskGates = @(rg -o --no-filename 'GetEnvironmentVariable\("SWIFTEXPORT_RUN_[^"]+"' SwiftExport.Application.Tests | ForEach-Object { $_ -replace '^GetEnvironmentVariable\("','' -replace '"$','' } | Sort-Object -Unique)
if ($taskCategories.Count -ne 37 -or $taskGates.Count -ne 14) { throw '发现的数据库门禁或分类已变化，需重新检查范围。' }
$taskFilter = (@($taskCategories | ForEach-Object { 'TestCategory!=' + $_ }) + @('FullyQualifiedName!~RealPg_', 'FullyQualifiedName!~SqlServer', 'FullyQualifiedName!~LocalDbTests', 'FullyQualifiedName!~SqlTests')) -join '&'
$taskFilter += '&(FullyQualifiedName~AdminPasswordRecoveryTests|FullyQualifiedName~Infrastructure_RegistersDedicatedDictTypeRepository|FullyQualifiedName~TargetSystemServicesAndRepositories_HaveNoNotImplementedPlaceholder)'
$taskOriginalGates = @{}
$taskExit = 1
try {
    foreach ($taskGate in $taskGates) {
        $taskOriginalGates[$taskGate] = [Environment]::GetEnvironmentVariable($taskGate, 'Process')
        [Environment]::SetEnvironmentVariable($taskGate, '0', 'Process')
    }
    dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter $taskFilter --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p09-r03-t01-tools-dict-offline.trx' -v minimal
    $taskExit = $LASTEXITCODE
}
finally {
    foreach ($taskGate in $taskGates) { [Environment]::SetEnvironmentVariable($taskGate, $taskOriginalGates[$taskGate], 'Process') }
}
exit $taskExit



