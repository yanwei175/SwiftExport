# P09-R18：登记已验证的三个旧类型六源文件退出，核对原2390源码/项目快照；不重复测试、不连接数据库。
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskManifestPath='docs/postgresql-migration/任务结果/P09-当前源退出文件清单.json'
$taskManifest=Get-Content $taskManifestPath -Raw|ConvertFrom-Json
$taskNames=@('PackingListRepository','CommandReceiptRepository','SourceDeliveryQueryRepository')
foreach($taskEntry in @($taskManifest|Where-Object{$_.name -in $taskNames})){
    foreach($taskDefinition in $taskEntry.definitions){if(Test-Path -LiteralPath $taskDefinition.path){throw '旧源仍存在'}}
    $taskEntry.status='验证通过：P09-R18源链已退出，原箱单场景1真PG/必要构建通过'
    $taskEntry.verification+='；R18原箱单持久化/发票精确过滤与分页/审计失败回滚/命令重放1真PG362.6921ms通过；来源/客户/权限/日志端口沿用原夹具，头行/回执及搜索真实。SQL Server旧升级DDL属S退役，当前PG结构就绪及关联来源保存已验，不称PG升级幂等。50表52984旧行全摘要同，新14留存，序列96不变，连接锁进程0；三旧类型六文件固定SHA/HEAD/跟踪/外部消费者零复核后退出，最终构建0错误7旧警告5.91秒；最终源结构/包图/宿主待。'
}
$taskExited=@($taskManifest|Where-Object{$_.status -like '验证通过：P09-*'})
$taskExpectedMissing=@($taskExited|ForEach-Object{$_.definitions}|ForEach-Object{$_.path})
$taskFacts=Get-Content docs/postgresql-migration/任务结果/P09-当前源退出引用事实.json -Raw|ConvertFrom-Json
$taskProperties=@($taskFacts.source_hashes.PSObject.Properties)
$taskMissing=@(); $taskChanged=@(); $taskUnexpectedProduction=@(); $taskUnexpectedMissing=@(); $taskExisting=0
foreach($taskProperty in $taskProperties){
    $taskPath=$taskProperty.Name
    if(-not(Test-Path -LiteralPath $taskPath)){
        $taskMissing+=$taskPath
        if($taskPath -notin $taskExpectedMissing){$taskUnexpectedMissing+=$taskPath}
        continue
    }
    $taskExisting++
    if((Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash -ne $taskProperty.Value){
        $taskChanged+=$taskPath
        if($taskPath -notlike 'SwiftExport.Application.Tests/*' -and $taskPath -notin @('_tools/DbSeed/DbSeed.csproj','_tools/AdminPasswordRecovery/AdminPasswordRecovery.csproj')){$taskUnexpectedProduction+=$taskPath}
    }
}
$taskEnumerated=@(rg --files -g '*.cs' -g '*.csproj' -g '!弃用资料/**' -g '!**/obj/**' -g '!**/bin/**' -g '!.agents/**' -g '!.codex/**'|ForEach-Object{$_ -replace '\\','/'})
if($LASTEXITCODE -ne 0){throw '源码枚举失败'}
$taskFixedPaths=@($taskProperties|ForEach-Object{$_.Name})
$taskNew=@($taskEnumerated|Where-Object{$_ -notin $taskFixedPaths})
$taskIgnored=@($taskFixedPaths|Where-Object{$_ -notin $taskEnumerated -and $_ -notin $taskMissing})
$taskProtection=[ordered]@{
    recorded_at=(Get-Date).ToString('o');stage='P09-R18累计源码保护，箱单原持久化消费者转PG及三旧类型六文件退出'
    fixed_snapshot_source_count=$taskProperties.Count;source_candidate_types_exited=$taskExited.Count
    expected_missing_count=$taskExpectedMissing.Count;missing=$taskMissing;changed_files=$taskChanged
    unexpected_production_changes=$taskUnexpectedProduction;unexpected_missing=$taskUnexpectedMissing;new_sources=$taskNew
    current_enumerated_sources=$taskEnumerated.Count;existing_fixed_snapshot_files=$taskExisting
    ignored_existing_unchanged_artifacts=$taskIgnored;database_run=$true;source_deletions_this_batch=6
}
$taskProtection|ConvertTo-Json -Depth 10|Set-Content -LiteralPath docs/postgresql-migration/任务结果/P09-R18-源码保护证据.json -Encoding utf8
if($taskProperties.Count -ne 2390 -or $taskExited.Count -ne 70 -or $taskMissing.Count -ne 79 -or $taskUnexpectedProduction.Count -ne 0 -or $taskUnexpectedMissing.Count -ne 0 -or $taskNew.Count -ne 0){throw '源码保护不符合本批边界'}
rg -l -w -g '*.cs' -g '*.csproj' -g '!弃用资料/**' -g '!**/obj/**' -g '!**/bin/**' -g '!.agents/**' -g '!.codex/**' -- 'PackingListRepository|CommandReceiptRepository|SourceDeliveryQueryRepository' .
if($LASTEXITCODE -ne 1){throw '退出链仍有旧类型引用或搜索失败'}
$taskBlocked='SwiftExport.Infrastructure/Repositories/DemandPlanning/ExecutionEligibilityRepository.cs'
if(-not(Test-Path -LiteralPath $taskBlocked)){$taskBlocked=@(rg --files SwiftExport.Infrastructure -g ExecutionEligibilityRepository.cs)[0]}
if((Get-FileHash -LiteralPath $taskBlocked -Algorithm SHA256).Hash -ne '0DDAE30D90FE9F268665AF4394C96BE24473030936AE39EDB4B6192B7DAF9378'){throw '待确认原文件发生变化'}
$taskManifest|ConvertTo-Json -Depth 15|Set-Content -LiteralPath $taskManifestPath -Encoding utf8

$taskRecordPath='docs/postgresql-migration/任务结果/P09-R18-箱单原持久化-文件证据.json'
$taskRecord=Get-Content $taskRecordPath -Raw|ConvertFrom-Json
if((Get-FileHash -LiteralPath $taskRecord.test.path -Algorithm SHA256).Hash -ne $taskRecord.test.after_sha256){throw '已通过测试源又变化，不能复用通过事实'}
$taskRecord.status='本批完成：原箱单场景1真PG通过，三旧类型六文件退出；SQL Server旧升级DDL属S，整体P09未完成'
$taskRecord|Add-Member -NotePropertyName completed_at -NotePropertyValue (Get-Date).ToString('o')
$taskRecord|Add-Member -NotePropertyName execution -NotePropertyValue @{
    build_command='dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -v minimal'
    builds=@(@{exit=0;errors=0;existing_warnings=7;seconds=6.89;stage='原测试迁PG后必要编译'},@{exit=0;errors=0;existing_warnings=7;seconds=5.91;stage='三旧类型六文件退出后必要编译；测试/PG源未变，不重跑已绿实库'})
    test_command="& './docs/postgresql-migration/任务结果/P09-R18-箱单原持久化-实际命令.ps1'"
    tests=@{total=1;passed=1;failed=0;skipped=0;method_seconds=0.3626921;host_seconds='约2秒，含50表原数据保护'}
    artifacts=@('TestResults/postgresql-migration/p09-r18-packing-persistence-pg.trx','TestResults/postgresql-migration/p09-r18-packing-data-protection.json')|ForEach-Object{@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}}
    protected_tables=50;protected_old_rows=52984;original_rows_unchanged=$true;retained_added_rows=14;current_rows=52998;sequence_before=96;sequence_after=96
    gates_current=12;integration_categories_current=36;removed_source_gate='SWIFTEXPORT_RUN_PACKING_LOCALDB_TESTS';removed_source_category='SqlServerIntegration'
    historical_scripts='历史命令中的13门禁/37分类是当时实际范围，不改历史记录或照搬重放；后续命令必须按当前12/36重新核对。'
    local_command_issues='一处只读猜测PackingListDraftService.Persistence.cs不存在，未改文件或数据库，随后rg --files发现真实实现并读取；一次进程查询无匹配返回exit1而非测试/编译失败。'
    other_database_clients_after=0;advisory_locks_after=0;test_processes_after=@(Get-Process -Name testhost,vstest.console -ErrorAction SilentlyContinue).Count
    full_test_run=$false;repeated_green_database_run=$false;runtime_or_backup_write=$false;ddl=$false;pg_upgrade_idempotence_tested=$false;sql_server_upgrade_artifacts='保留待S源结构退役'
    commit_merge_push=$false
}
$taskRecord|ConvertTo-Json -Depth 17|Set-Content -LiteralPath $taskRecordPath -Encoding utf8
[pscustomobject]@{OriginalSources=$taskProperties.Count;ExitedTypes=$taskExited.Count;ExitedFiles=$taskMissing.Count;Remaining=76-$taskExited.Count;ChangedTests=@($taskChanged|Where-Object{$_ -like 'SwiftExport.Application.Tests/*'}).Count;UnexpectedProduction=$taskUnexpectedProduction.Count;UnexpectedMissing=$taskUnexpectedMissing.Count;NewSources=$taskNew.Count;IgnoredExisting=$taskIgnored.Count;TestProcesses=$taskRecord.execution.test_processes_after}|Format-List
