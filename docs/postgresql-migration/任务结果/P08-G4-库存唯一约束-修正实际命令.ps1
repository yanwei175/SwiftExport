# 本批唯一已登记原生测试命令；全部夹具写入由既有外层事务finally回滚。
$ErrorActionPreference='Stop'
$taskGuardDoc=Get-Content -LiteralPath SwiftExport.WebApi/appsettings.json -Raw | ConvertFrom-Json
$taskGuardSettings=[System.Data.Common.DbConnectionStringBuilder]::new()
$taskGuardSettings.set_ConnectionString([string]$taskGuardDoc.ConnectionStrings.Default)
if ([string]$taskGuardSettings.get_Item('Host') -ne '192.168.3.12' -or [string]$taskGuardSettings.get_Item('Database') -ne 'dv_next') { throw '库存唯一约束验证实例不符' }
$taskGuardSettings.set_Item('Database','dv_next_pg_failure_tests_20261004')
$taskGuardNames=@('SWIFTEXPORT_RUN_PG_FAILURE_TESTS','SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION')
$taskGuardPrior=@{}
foreach($taskGuardName in $taskGuardNames){$taskGuardPrior[$taskGuardName]=[Environment]::GetEnvironmentVariable($taskGuardName,'Process')}
$taskGuardExit=1
try{
 [Environment]::SetEnvironmentVariable($taskGuardNames[0],'1','Process')
 [Environment]::SetEnvironmentVariable($taskGuardNames[1],$taskGuardSettings.get_ConnectionString(),'Process')
 dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~RealPg_ResumedInventoryPersistence' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p08-g4-inventory-guards-corrected.trx' -v minimal
 $taskGuardExit=$LASTEXITCODE
}finally{
 foreach($taskGuardName in $taskGuardNames){[Environment]::SetEnvironmentVariable($taskGuardName,$taskGuardPrior[$taskGuardName],'Process')}
 $taskGuardSettings=$null
 $taskGuardDoc=$null
}
exit $taskGuardExit
