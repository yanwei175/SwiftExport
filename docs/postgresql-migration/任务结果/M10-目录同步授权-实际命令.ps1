# M10 目录/授权两次序：原独立RBAC空目录首次；完整新目录和审计留存，本人随机可变夹具精确清理，原96行全保护。
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
$taskBuilder.set_Item('Database','dv_next_pg_rbac_tests_20261005')
$taskBuilder.set_Item('Search Path','public')
$taskGates=@(rg -o --no-filename 'GetEnvironmentVariable\("SWIFTEXPORT_RUN_[^"]+"' SwiftExport.Application.Tests|ForEach-Object{$_ -replace '^GetEnvironmentVariable\("','' -replace '"$',''}|Sort-Object -Unique)
if($taskGates.Count -ne 14){throw '数据库门禁数量已变化，先检查范围。'}
$taskNames=@($taskGates)+@('SWIFTEXPORT_PG_RBAC_TEST_CONNECTION','SWIFTEXPORT_PG_RBAC_CATALOG_BATCH')
$taskOriginal=@{}
$taskExit=1
try{
    foreach($taskName in $taskNames){$taskOriginal[$taskName]=[Environment]::GetEnvironmentVariable($taskName,'Process')}
    foreach($taskGate in $taskGates){[Environment]::SetEnvironmentVariable($taskGate,'0','Process')}
    [Environment]::SetEnvironmentVariable('SWIFTEXPORT_RUN_PG_RBAC_TESTS','1','Process')
    [Environment]::SetEnvironmentVariable('SWIFTEXPORT_PG_RBAC_TEST_CONNECTION',$taskBuilder.get_ConnectionString(),'Process')
    [Environment]::SetEnvironmentVariable('SWIFTEXPORT_PG_RBAC_CATALOG_BATCH','M10-CATALOG-ROLE-20261006','Process')
    dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~RolePermissionsAndCatalogSync_KeepBothServiceOrdersConsistent' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m10-role-permissions-catalog-sync-native.trx' -v minimal
    $taskExit=$LASTEXITCODE
}
finally{
    foreach($taskName in $taskNames){[Environment]::SetEnvironmentVariable($taskName,$taskOriginal[$taskName],'Process')}
}
exit $taskExit

