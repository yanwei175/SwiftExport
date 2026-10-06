# M05 Excel配置真实快照/版本：仅failure新1配置3字段保留，原服务写入；不修改/清理旧资料或结构。
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
$taskNames=@($taskGates)+@('SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION','SWIFTEXPORT_PG_RUNTIME_EXCEL_PROFILE_BATCH')
$taskOriginal=@{}
$taskExit=1
try{
    foreach($taskName in $taskNames){$taskOriginal[$taskName]=[Environment]::GetEnvironmentVariable($taskName,'Process')}
    foreach($taskGate in $taskGates){[Environment]::SetEnvironmentVariable($taskGate,'0','Process')}
    [Environment]::SetEnvironmentVariable('SWIFTEXPORT_RUN_PG_FAILURE_TESTS','1','Process')
    [Environment]::SetEnvironmentVariable('SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION',$taskBuilder.get_ConnectionString(),'Process')
    [Environment]::SetEnvironmentVariable('SWIFTEXPORT_PG_RUNTIME_EXCEL_PROFILE_BATCH','02e119c9-bec4-4b8f-a9c3-9dd718b5e184','Process')
    dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~RealPg_RuntimeExcelProfileSnapshotAndVersionCompetition' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m05-excel-profile-snapshot-version-native.trx' -v minimal -- MSTest.Parallelize.Workers=1
    $taskExit=$LASTEXITCODE
}
finally{
    foreach($taskName in $taskNames){[Environment]::SetEnvironmentVariable($taskName,$taskOriginal[$taskName],'Process')}
}
exit $taskExit
