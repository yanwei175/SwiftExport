# P09-S04：只核验及生成本地PG评审文本，原SQL基线仍由旧测试消费，不连接或部署数据库。
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskRecordPath='docs/postgresql-migration/任务结果/P09-S04-旧结构生成分支-文件证据.json'
$taskRecord=Get-Content $taskRecordPath -Raw|ConvertFrom-Json
foreach($taskBaseline in $taskRecord.baselines){
    if((Get-FileHash -LiteralPath $taskBaseline.path).Hash -ne $taskBaseline.before_sha256){throw '运行前基线SHA变化，停止'}
}
$taskStarted=Get-Date
dotnet run --project _tools/SchemaGenerator/SchemaGenerator.csproj --no-build --no-restore -- --verify
if($LASTEXITCODE -ne 0){throw '当前PG漂移检查失败，不重写以隐藏漂移'}
dotnet run --project _tools/SchemaGenerator/SchemaGenerator.csproj --no-build --no-restore
if($LASTEXITCODE -ne 0){throw '当前PG本地生成失败'}
foreach($taskBaseline in $taskRecord.baselines){
    $taskBaseline|Add-Member -NotePropertyName after_sha256 -NotePropertyValue (Get-FileHash -LiteralPath $taskBaseline.path).Hash
    if($taskBaseline.after_sha256 -ne $taskBaseline.before_sha256){throw '生成后基线字节变化，停止收口'}
}
$taskRecord|Add-Member -NotePropertyName execution -NotePropertyValue @{
    started_at=$taskStarted.ToString('o');finished_at=(Get-Date).ToString('o')
    build_command='dotnet build _tools/SchemaGenerator/SchemaGenerator.csproj --no-restore -v minimal'
    build_exit=0;build_errors=0;existing_nuget_environment_warnings=1;build_seconds=1.51
    verify_command='dotnet run --project _tools/SchemaGenerator/SchemaGenerator.csproj --no-build --no-restore -- --verify';verify_exit=0
    generate_command='dotnet run --project _tools/SchemaGenerator/SchemaGenerator.csproj --no-build --no-restore';generate_exit=0
    database_read_or_write=$false;ddl=$false;package_or_configuration_change=$false;pg_generator_change=$false
    full_test_run=$false;repeated_green_test=$false;new_test_matrix=$false;commit_merge_push=$false
    command_issues='多文件apply_patch返回失败，但检查发现首文件Program已有全部预期改动、README尚未改；未重放首文件，随后仅完成README，删除尚未发生前重新核对旧源SHA/HEAD/消费者再获准删除。只读路径/输出限制已收敛。'
}
$taskRecord.status='代码完成待源码保护收口：当前PG生成/verify通过，两基线SHA不变'
$taskRecord|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $taskRecordPath -Encoding utf8
'Current PG generation/verify succeeded; both baseline bytes preserved, no database access.'
