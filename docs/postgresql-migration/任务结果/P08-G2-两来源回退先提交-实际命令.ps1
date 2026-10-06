# 本批已登记一次性实际服务竞争；保留全部成功新事实，不清库或重新执行。
$ErrorActionPreference='Stop'
$taskCrossDoc=Get-Content -LiteralPath SwiftExport.WebApi/appsettings.json -Raw | ConvertFrom-Json
$taskCrossSettings=[System.Data.Common.DbConnectionStringBuilder]::new()
$taskCrossSettings.set_ConnectionString([string]$taskCrossDoc.ConnectionStrings.Default)
if ([string]$taskCrossSettings.get_Item('Host') -ne '192.168.3.12' -or [string]$taskCrossSettings.get_Item('Database') -ne 'dv_next') { throw '两来源回退验证实例不符' }
$taskCrossSettings.set_Item('Database','dv_next_pg_failure_tests_20261004')
$taskCrossNames=@('SWIFTEXPORT_RUN_PG_FAILURE_TESTS','SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION','SWIFTEXPORT_PG_RUNTIME_CROSS_SOURCE_BATCH')
$taskCrossPrior=@{}
foreach($taskCrossName in $taskCrossNames){$taskCrossPrior[$taskCrossName]=[Environment]::GetEnvironmentVariable($taskCrossName,'Process')}
$taskCrossExit=1
try{
 [Environment]::SetEnvironmentVariable($taskCrossNames[0],'1','Process')
 [Environment]::SetEnvironmentVariable($taskCrossNames[1],$taskCrossSettings.get_ConnectionString(),'Process')
 [Environment]::SetEnvironmentVariable($taskCrossNames[2],'eac1e32d-21b7-4f6e-84d1-a7a66ae10700','Process')
 dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~RealPg_RuntimeCrossSourceRollbackBeforeAssociation' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p08-g2-cross-source-rollback-first-native.trx' -v minimal
 $taskCrossExit=$LASTEXITCODE
}finally{
 foreach($taskCrossName in $taskCrossNames){[Environment]::SetEnvironmentVariable($taskCrossName,$taskCrossPrior[$taskCrossName],'Process')}
 $taskCrossSettings=$null
 $taskCrossDoc=$null
}
exit $taskCrossExit
