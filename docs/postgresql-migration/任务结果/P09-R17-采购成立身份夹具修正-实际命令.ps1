# P09-R17：原SQL Server同样保护成立身份，夹具改为INSERT预设来源/执行身份；共用Initialize已变，因此重跑两个受影响原方法，无新增矩阵/DDL。
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
if($taskGates.Count -ne 13){throw '数据库门禁数量已变化，先检查范围。'}
$taskNames=@($taskGates)+@('SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION')
$taskOriginal=@{}
$taskExit=1
try{
    foreach($taskName in $taskNames){$taskOriginal[$taskName]=[Environment]::GetEnvironmentVariable($taskName,'Process')}
    foreach($taskGate in $taskGates){[Environment]::SetEnvironmentVariable($taskGate,'0','Process')}
    [Environment]::SetEnvironmentVariable('SWIFTEXPORT_RUN_PG_FAILURE_TESTS','1','Process')
    [Environment]::SetEnvironmentVariable('SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION',$taskBuilder.get_ConnectionString(),'Process')
    $taskRunStarted = Get-Date
    dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~PurchaseDraftAppendSqlTests' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p09-r17-draft-append-after-identity-seed-pg.trx' -v minimal
    $taskExit=$LASTEXITCODE
    $taskProofSource = 'SwiftExport.Application.Tests/bin/Debug/net10.0/TestResults/postgresql-migration/p09-r17-draft-data-protection.json'
    if ((Test-Path -LiteralPath $taskProofSource) -and (Get-Item -LiteralPath $taskProofSource).LastWriteTime -gt $taskRunStarted) {
        Copy-Item -LiteralPath $taskProofSource -Destination 'TestResults/postgresql-migration/p09-r17-draft-data-protection.json'
    } elseif ($taskExit -eq 0) { throw '测试通过但本轮原数据保护证据缺失，不能验收。' }
}
finally{
    foreach($taskName in $taskNames){[Environment]::SetEnvironmentVariable($taskName,$taskOriginal[$taskName],'Process')}
}
exit $taskExit
