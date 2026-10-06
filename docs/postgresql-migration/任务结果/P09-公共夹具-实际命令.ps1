# 用户已批准单固定新库、逐例导出事实及限定public重置；本脚本只开启该门禁，不输出凭据。
param([string]$TestFilter='FullyQualifiedName~ForExcelPurchaseOrderSqlTests|FullyQualifiedName~AfterSalesSqlTests|FullyQualifiedName~AfterSalesComplaintNumberSqlTests', [string]$RunName='p09-legacy-first-batch')
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskSettings=Get-Content SwiftExport.WebApi/appsettings.json -Raw|ConvertFrom-Json
$taskBuilder=[System.Data.Common.DbConnectionStringBuilder]::new()
$taskBuilder.set_ConnectionString($taskSettings.ConnectionStrings.Default)
if([string]$taskBuilder.get_Item('Host') -ne '192.168.3.12' -or [string]$taskBuilder.get_Item('Port') -ne '5432' -or [string]$taskBuilder.get_Item('Database') -ne 'dv_next'){throw '原配置不匹配已授权PG实例，不输出凭据。'}
$taskBuilder.set_Item('Database','dv_next_pg_legacy_tests_20261006')
$taskBuilder.set_Item('Search Path','public')
$taskOriginalConnection=[Environment]::GetEnvironmentVariable('SWIFTEXPORT_PG_LEGACY_TEST_CONNECTION','Process')
$taskOriginalGate=[Environment]::GetEnvironmentVariable('SWIFTEXPORT_RUN_PG_LEGACY_TESTS','Process')
$taskExit=1
try{
 [Environment]::SetEnvironmentVariable('SWIFTEXPORT_PG_LEGACY_TEST_CONNECTION',$taskBuilder.get_ConnectionString(),'Process')
 [Environment]::SetEnvironmentVariable('SWIFTEXPORT_RUN_PG_LEGACY_TESTS','1','Process')
 dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter $TestFilter --results-directory TestResults/postgresql-migration --logger "trx;LogFileName=$RunName.trx" -v normal 2>&1 | Tee-Object -FilePath "TestResults/postgresql-migration/$RunName.log"
 $taskExit=$LASTEXITCODE
}finally{
 [Environment]::SetEnvironmentVariable('SWIFTEXPORT_PG_LEGACY_TEST_CONNECTION',$taskOriginalConnection,'Process')
 [Environment]::SetEnvironmentVariable('SWIFTEXPORT_RUN_PG_LEGACY_TESTS',$taskOriginalGate,'Process')
}
exit $taskExit
