# P09-B1统一编译与受影响PG协议验证；仅常量SELECT，无业务表读取/写入或DDL。
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskConnection=[Environment]::GetEnvironmentVariable('SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION','Process')
if([string]::IsNullOrWhiteSpace($taskConnection)){
    $taskSettings=Get-Content SwiftExport.WebApi/appsettings.json -Raw|ConvertFrom-Json
    $taskBuilder=[System.Data.Common.DbConnectionStringBuilder]::new()
    $taskBuilder.set_ConnectionString($taskSettings.ConnectionStrings.Default)
    if([string]$taskBuilder.get_Item('Host') -ne '192.168.3.12' -or [string]$taskBuilder.get_Item('Port') -ne '5432' -or [string]$taskBuilder.get_Item('Database') -ne 'dv_next'){throw '原配置不匹配已授权PG实例，不输出凭据。'}
    $taskBuilder.set_Item('Database','dv_next_pg_failure_tests_20261004')
    $taskBuilder.set_Item('Search Path','public')
    $taskConnection=$taskBuilder.get_ConnectionString()
}
$taskOriginalConnection=[Environment]::GetEnvironmentVariable('SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION','Process')
$taskOriginalGate=[Environment]::GetEnvironmentVariable('SWIFTEXPORT_RUN_PG_FAILURE_TESTS','Process')
$taskExit=1
try{
    [Environment]::SetEnvironmentVariable('SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION',$taskConnection,'Process')
    [Environment]::SetEnvironmentVariable('SWIFTEXPORT_RUN_PG_FAILURE_TESTS','1','Process')
    dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore --filter 'FullyQualifiedName~PostgreSqlTraceReadProtocolIntegrationTests' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p09-batch-protocol-retirement-pg.trx' -v minimal
    $taskExit=$LASTEXITCODE
}finally{
    [Environment]::SetEnvironmentVariable('SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION',$taskOriginalConnection,'Process')
    [Environment]::SetEnvironmentVariable('SWIFTEXPORT_RUN_PG_FAILURE_TESTS',$taskOriginalGate,'Process')
}
exit $taskExit
