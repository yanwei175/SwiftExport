# P09-R15：三个文件的PG禁止依赖/来源白名单标识符断言补齐后，13现有相关离线场景；真实Dapper命令仅记录，不连接数据库或新增矩阵。
$ErrorActionPreference = 'Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskCategories = @(rg -o --no-filename 'TestCategory\("[^"]+"' SwiftExport.Application.Tests | ForEach-Object { $_ -replace '^TestCategory\("','' -replace '"$','' } | Where-Object { $_ -match 'Integration$' } | Sort-Object -Unique)
$taskGates = @(rg -o --no-filename 'GetEnvironmentVariable\("SWIFTEXPORT_RUN_[^"]+"' SwiftExport.Application.Tests | ForEach-Object { $_ -replace '^GetEnvironmentVariable\("','' -replace '"$','' } | Sort-Object -Unique)
if ($taskCategories.Count -ne 37 -or $taskGates.Count -ne 13) { throw '发现的数据库门禁或分类已变化，需重新检查范围。' }
$taskFilter = (@($taskCategories | ForEach-Object { 'TestCategory!=' + $_ }) + @('FullyQualifiedName!~RealPg_', 'FullyQualifiedName!~SqlServer', 'FullyQualifiedName!~LocalDbTests', 'FullyQualifiedName!~SqlTests')) -join '&'
$taskFilter += '&(FullyQualifiedName~PackingListDraftApiContractTests|FullyQualifiedName~PackingListSourceQueryTests|FullyQualifiedName~SourceDemandDeliveryContractTests)'
$taskOriginalGates = @{}
$taskExit = 1
try {
    foreach ($taskGate in $taskGates) {
        $taskOriginalGates[$taskGate] = [Environment]::GetEnvironmentVariable($taskGate, 'Process')
        [Environment]::SetEnvironmentVariable($taskGate, '0', 'Process')
    }
    dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter $taskFilter --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p09-r15-shipping-delivery-consumers-offline.trx' -v minimal
    $taskExit = $LASTEXITCODE
}
finally {
    foreach ($taskGate in $taskGates) { [Environment]::SetEnvironmentVariable($taskGate, $taskOriginalGates[$taskGate], 'Process') }
}
exit $taskExit
