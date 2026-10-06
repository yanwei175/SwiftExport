$ErrorActionPreference='Stop'
$taskSettingsDoc=Get-Content SwiftExport.WebApi/appsettings.json -Raw | ConvertFrom-Json
$taskPgSettings=[System.Data.Common.DbConnectionStringBuilder]::new()
$taskPgSettings.set_ConnectionString([string]$taskSettingsDoc.ConnectionStrings.Default)
if ([string]$taskPgSettings['Host'] -ne '192.168.3.12' -or [string]$taskPgSettings['Database'] -ne 'dv_next') { throw '连接目标与本批登记不一致' }
$taskPgSettings.set_Item('Database','dv_next_pg_failure_tests_20261004')
$taskNames=@('SWIFTEXPORT_RUN_PG_FAILURE_TESTS','SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION','SWIFTEXPORT_PG_RUNTIME_AFTERSALES_BATCH')
$taskPrevious=@{}
foreach($taskName in $taskNames){ $taskPrevious[$taskName]=[Environment]::GetEnvironmentVariable($taskName,'Process') }
$taskExit=1
try {
 [Environment]::SetEnvironmentVariable($taskNames[0],'1','Process')
 [Environment]::SetEnvironmentVariable($taskNames[1],$taskPgSettings.get_ConnectionString(),'Process')
 [Environment]::SetEnvironmentVariable($taskNames[2],'18bd9715-a372-4d43-9569-76f5cd25c500','Process')
 dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~RealPg_RuntimeAfterSalesConfirmedSourceExit' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m25-aftersales-confirmed-source-exit.trx' -v minimal
 $taskExit=$LASTEXITCODE
} finally {
 foreach($taskName in $taskNames){ [Environment]::SetEnvironmentVariable($taskName,$taskPrevious[$taskName],'Process') }
 $taskPgSettings=$null; $taskSettingsDoc=$null
}
exit $taskExit
