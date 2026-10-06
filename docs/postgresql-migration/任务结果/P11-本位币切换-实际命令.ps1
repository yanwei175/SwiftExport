# 从已授权配置读取凭据，仅开启固定隔离库和旧failure库的回滚测试，不输出连接字符串。
param([string]$RunName='p11-base-currency-reset-real-pg')
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskSettings=Get-Content SwiftExport.WebApi/appsettings.json -Raw|ConvertFrom-Json
$taskBuilder=[System.Data.Common.DbConnectionStringBuilder]::new()
$taskBuilder.set_ConnectionString($taskSettings.ConnectionStrings.Default)
if([string]$taskBuilder.get_Item('Host') -ne '192.168.3.12' -or [string]$taskBuilder.get_Item('Port') -ne '5432' -or [string]$taskBuilder.get_Item('Database') -ne 'dv_next'){throw '原配置不匹配已授权PG实例，不输出凭据。'}
$taskBuilder.set_Item('Database','dv_next_pg_failure_tests_20261004')
$taskBuilder.set_Item('Search Path','public')
$taskOriginalConnection=[Environment]::GetEnvironmentVariable('SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION','Process')
$taskOriginalGate=[Environment]::GetEnvironmentVariable('SWIFTEXPORT_RUN_PG_FAILURE_TESTS','Process')
$taskExit=1
try{
 [Environment]::SetEnvironmentVariable('SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION',$taskBuilder.get_ConnectionString(),'Process')
 [Environment]::SetEnvironmentVariable('SWIFTEXPORT_RUN_PG_FAILURE_TESTS','1','Process')
 & ./docs/postgresql-migration/任务结果/P09-公共夹具-实际命令.ps1 -TestFilter 'FullyQualifiedName~BaseCurrencySwitch_ResetsRatesAndRemindsUser|FullyQualifiedName~RealPg_ResumedCurrencyQueriesAndDeferredReset' -RunName $RunName
 $taskExit=$LASTEXITCODE
}finally{
 [Environment]::SetEnvironmentVariable('SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION',$taskOriginalConnection,'Process')
 [Environment]::SetEnvironmentVariable('SWIFTEXPORT_RUN_PG_FAILURE_TESTS',$taskOriginalGate,'Process')
}
exit $taskExit
