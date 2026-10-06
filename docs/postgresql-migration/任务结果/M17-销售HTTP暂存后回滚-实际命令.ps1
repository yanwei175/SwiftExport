# M17 销售HTTP暂存后回滚单项真实PG：只在已授权failure库保留本批新事实；不重置、不清理原数据或重跑成功批次。
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskBaseConnection=[Environment]::GetEnvironmentVariable('SWIFTEXPORT_PG_TEST_CONNECTION','Process')
if([string]::IsNullOrWhiteSpace($taskBaseConnection)){
    $taskSettings=Get-Content SwiftExport.WebApi/appsettings.json -Raw|ConvertFrom-Json
    $taskBaseConnection=$taskSettings.ConnectionStrings.Default
}
$taskBuilder=[System.Data.Common.DbConnectionStringBuilder]::new()
$taskBuilder.set_ConnectionString($taskBaseConnection)
if([string]$taskBuilder.get_Item('Host') -ne '192.168.3.12' -or [string]$taskBuilder.get_Item('Port') -ne '5432' -or [string]$taskBuilder.get_Item('Database') -ne 'dv_next'){
    throw '原配置不匹配已授权PG实例/运行库，停止且不输出凭据。'
}
$taskBuilder.set_Item('Database','dv_next_pg_failure_tests_20261004')
$taskBuilder.set_Item('Search Path','public')
$taskGates=@(rg -o --no-filename 'GetEnvironmentVariable\("SWIFTEXPORT_RUN_[^"]+"' SwiftExport.Application.Tests|ForEach-Object{$_ -replace '^GetEnvironmentVariable\("','' -replace '"$',''}|Sort-Object -Unique)
if($taskGates.Count -ne 14){throw '数据库门禁数量已变化，先检查范围。'}
$taskNames=@($taskGates)+@('SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION','SWIFTEXPORT_PG_RUNTIME_SALES_HTTP_BATCH','SWIFTEXPORT_PG_RUNTIME_SALES_HTTP_RESUME_HEAD')
$taskOriginal=@{}
$taskExit=1
try{
    foreach($taskName in $taskNames){$taskOriginal[$taskName]=[Environment]::GetEnvironmentVariable($taskName,'Process')}
    foreach($taskGate in $taskGates){[Environment]::SetEnvironmentVariable($taskGate,'0','Process')}
    [Environment]::SetEnvironmentVariable('SWIFTEXPORT_RUN_PG_FAILURE_TESTS','1','Process')
    [Environment]::SetEnvironmentVariable('SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION',$taskBuilder.get_ConnectionString(),'Process')
    [Environment]::SetEnvironmentVariable('SWIFTEXPORT_PG_RUNTIME_SALES_HTTP_BATCH','a0c0d521-48fc-48c2-bebf-da738188f23a','Process')
    [Environment]::SetEnvironmentVariable('SWIFTEXPORT_PG_RUNTIME_SALES_HTTP_RESUME_HEAD','78db0c78-2af7-4606-9713-4a3504451697','Process')
    dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~RealPg_RuntimeSalesHttpStageFailureRollsBackAndRecovers' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m17-sales-http-stage-rollback-native.trx' -v minimal
    $taskExit=$LASTEXITCODE
}
finally{
    foreach($taskName in $taskNames){[Environment]::SetEnvironmentVariable($taskName,$taskOriginal[$taskName],'Process')}
}
exit $taskExit
