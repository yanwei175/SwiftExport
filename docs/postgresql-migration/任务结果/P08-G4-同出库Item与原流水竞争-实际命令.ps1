# 本批一次性已登记实际命令；成功新事实及号段保留，不清库或再次运行。
$ErrorActionPreference='Stop'
$taskSameDoc=Get-Content -LiteralPath SwiftExport.WebApi/appsettings.json -Raw | ConvertFrom-Json
$taskSameSettings=[System.Data.Common.DbConnectionStringBuilder]::new()
$taskSameSettings.set_ConnectionString([string]$taskSameDoc.ConnectionStrings.Default)
if ([string]$taskSameSettings.get_Item('Host') -ne '192.168.3.12' -or [string]$taskSameSettings.get_Item('Database') -ne 'dv_next') { throw '同出库竞争验证实例不符' }
$taskSameSettings.set_Item('Database','dv_next_pg_failure_tests_20261004')
$taskSameNames=@('SWIFTEXPORT_RUN_PG_FAILURE_TESTS','SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION','SWIFTEXPORT_PG_RUNTIME_SAME_OUTBOUND_BATCH')
$taskSamePrior=@{}
foreach($taskSameName in $taskSameNames){$taskSamePrior[$taskSameName]=[Environment]::GetEnvironmentVariable($taskSameName,'Process')}
$taskSameExit=1
try{
 [Environment]::SetEnvironmentVariable($taskSameNames[0],'1','Process')
 [Environment]::SetEnvironmentVariable($taskSameNames[1],$taskSameSettings.get_ConnectionString(),'Process')
 [Environment]::SetEnvironmentVariable($taskSameNames[2],'0a77ecca-8ebd-4da1-812f-a8f1c1f85f00','Process')
 dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~RealPg_RuntimeSameOutboundItemAndOriginalReversalCompetition' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p08-g4-same-outbound-native.trx' -v minimal
 $taskSameExit=$LASTEXITCODE
}finally{
 foreach($taskSameName in $taskSameNames){[Environment]::SetEnvironmentVariable($taskSameName,$taskSamePrior[$taskSameName],'Process')}
 $taskSameSettings=$null
 $taskSameDoc=$null
}
exit $taskSameExit
