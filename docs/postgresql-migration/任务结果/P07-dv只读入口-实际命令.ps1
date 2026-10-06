$ErrorActionPreference='Stop'
$taskDvConfig=Get-Content -LiteralPath SwiftExport.WebApi/appsettings.json -Raw | ConvertFrom-Json
$taskDvBuilder=[System.Data.Common.DbConnectionStringBuilder]::new()
$taskDvBuilder.set_ConnectionString([string]$taskDvConfig.ConnectionStrings.Default)
if ([string]$taskDvBuilder.get_Item('Host') -ne '192.168.3.12' -or [string]$taskDvBuilder.get_Item('Database') -ne 'dv_next') { throw '只读宿主连接目标不符' }
$taskDvBuilder.set_Item('Options','-c default_transaction_read_only=on')
$taskDvNames=@('SWIFTEXPORT_RUN_PG_TESTS','SWIFTEXPORT_PG_TEST_CONNECTION','SWIFTEXPORT_PG_RUNTIME_CURRENT_READ_ONLY_BATCH')
$taskDvPrevious=@{}
foreach($taskDvName in $taskDvNames){$taskDvPrevious[$taskDvName]=[Environment]::GetEnvironmentVariable($taskDvName,'Process')}
$taskDvExit=1
try{
 [Environment]::SetEnvironmentVariable($taskDvNames[0],'1','Process')
 [Environment]::SetEnvironmentVariable($taskDvNames[1],$taskDvBuilder.get_ConnectionString(),'Process')
 [Environment]::SetEnvironmentVariable($taskDvNames[2],'682ccdc0-1804-4c79-93da-9125c3703500','Process')
 dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~RealPg_RuntimeCurrentConfigurationReadOnlyHttp' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p07-dv-readonly-http.trx' -v minimal
 $taskDvExit=$LASTEXITCODE
}finally{
 foreach($taskDvName in $taskDvNames){[Environment]::SetEnvironmentVariable($taskDvName,$taskDvPrevious[$taskDvName],'Process')}
 $taskDvBuilder=$null
 $taskDvConfig=$null
}
exit $taskDvExit
