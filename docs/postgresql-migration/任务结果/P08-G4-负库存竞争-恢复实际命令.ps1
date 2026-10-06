# 已执行成功的历史命令；新增事实已保留，不再次运行本场景或清库重跑。
$ErrorActionPreference='Stop'
$taskNegativeDoc=Get-Content -LiteralPath SwiftExport.WebApi/appsettings.json -Raw | ConvertFrom-Json
$taskNegativeSettings=[System.Data.Common.DbConnectionStringBuilder]::new()
$taskNegativeSettings.set_ConnectionString([string]$taskNegativeDoc.ConnectionStrings.Default)
if ([string]$taskNegativeSettings.get_Item('Host') -ne '192.168.3.12' -or [string]$taskNegativeSettings.get_Item('Database') -ne 'dv_next') { throw '负库存验证实例不符' }
$taskNegativeSettings.set_Item('Database','dv_next_pg_failure_tests_20261004')
$taskNegativeNames=@('SWIFTEXPORT_RUN_PG_FAILURE_TESTS','SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION','SWIFTEXPORT_PG_RUNTIME_NEGATIVE_STOCK_BATCH')
$taskNegativePrior=@{}
foreach($taskNegativeName in $taskNegativeNames){$taskNegativePrior[$taskNegativeName]=[Environment]::GetEnvironmentVariable($taskNegativeName,'Process')}
$taskNegativeExit=1
try{
 [Environment]::SetEnvironmentVariable($taskNegativeNames[0],'1','Process')
 [Environment]::SetEnvironmentVariable($taskNegativeNames[1],$taskNegativeSettings.get_ConnectionString(),'Process')
 [Environment]::SetEnvironmentVariable($taskNegativeNames[2],'9935668e-0c0c-4399-b6e2-46985bdbcc00','Process')
 dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~RealPg_RuntimeOutboundNegativeStockCompetition' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p08-g4-negative-dv-native.trx' -v minimal
 $taskNegativeExit=$LASTEXITCODE
}finally{
 foreach($taskNegativeName in $taskNegativeNames){[Environment]::SetEnvironmentVariable($taskNegativeName,$taskNegativePrior[$taskNegativeName],'Process')}
 $taskNegativeSettings=$null
 $taskNegativeDoc=$null
}
exit $taskNegativeExit
