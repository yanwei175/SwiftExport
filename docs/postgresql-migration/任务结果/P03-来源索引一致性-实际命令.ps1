$ErrorActionPreference='Stop'
$taskSourceDoc=Get-Content -LiteralPath SwiftExport.WebApi/appsettings.json -Raw | ConvertFrom-Json
$taskSourceSettings=[System.Data.Common.DbConnectionStringBuilder]::new()
$taskSourceSettings.set_ConnectionString([string]$taskSourceDoc.ConnectionStrings.Default)
if ([string]$taskSourceSettings.get_Item('Host') -ne '192.168.3.12' -or [string]$taskSourceSettings.get_Item('Database') -ne 'dv_next') { throw '来源索引验证实例不符' }
$taskSourceSettings.set_Item('Database','dv_next_pg_failure_tests_20261004')
$taskSourceNames=@('SWIFTEXPORT_RUN_PG_FAILURE_TESTS','SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION')
$taskSourcePrior=@{}
foreach($taskSourceName in $taskSourceNames){$taskSourcePrior[$taskSourceName]=[Environment]::GetEnvironmentVariable($taskSourceName,'Process')}
$taskSourceExit=1
try{
 [Environment]::SetEnvironmentVariable($taskSourceNames[0],'1','Process')
 [Environment]::SetEnvironmentVariable($taskSourceNames[1],$taskSourceSettings.get_ConnectionString(),'Process')
 dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~GrossSource_UniquenessMatchesQueryComparison' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p03-source-index-green.trx' -v minimal
 $taskSourceExit=$LASTEXITCODE
}finally{
 foreach($taskSourceName in $taskSourceNames){[Environment]::SetEnvironmentVariable($taskSourceName,$taskSourcePrior[$taskSourceName],'Process')}
 $taskSourceSettings=$null
 $taskSourceDoc=$null
}
exit $taskSourceExit
