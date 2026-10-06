# P09-S02B：原单位快照接PG；核对2390原源和此前F01/F02精准范围、本批数据与新程序集，复用通过事实，不连接库或重跑测试。
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskManifestPath='docs/postgresql-migration/任务结果/P09-当前源退出文件清单.json'
$taskManifest=Get-Content $taskManifestPath -Raw|ConvertFrom-Json
$taskF01=Get-Content 'docs/postgresql-migration/任务结果/P09-F01-旧通用仓储退出-文件证据.json' -Raw|ConvertFrom-Json
$taskF02=Get-Content 'docs/postgresql-migration/任务结果/P09-F02-源连接分类器退出-文件证据.json' -Raw|ConvertFrom-Json
$taskAudit=$taskF01.audit_comment
if((Get-FileHash -LiteralPath $taskAudit.path).Hash -ne $taskAudit.after_sha256){throw '此前已验审计XML精准范围变化'}
$taskFactory='SwiftExport.Infrastructure/Data/DbConnectionFactory.cs'
if((Get-FileHash -LiteralPath $taskFactory).Hash -ne $taskF02.factory_after_sha256){throw '此前已验混合工厂精准范围变化'}
$taskExited=@($taskManifest|Where-Object{$_.status -like '验证通过：P09-*'})
$taskExpectedMissing=@($taskExited|Where-Object{$_.name -ne 'SqlConnectionFactory'}|ForEach-Object{$_.definitions}|ForEach-Object{$_.path})
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
        if($taskPath -notlike 'SwiftExport.Application.Tests/*' -and $taskPath -notin @('_tools/DbSeed/DbSeed.csproj','_tools/AdminPasswordRecovery/AdminPasswordRecovery.csproj','SwiftExport.Infrastructure/Services/Auth/AuditStampService.cs','SwiftExport.Infrastructure/Data/DbConnectionFactory.cs')){$taskUnexpectedProduction+=$taskPath}
    }
}
$taskEnumerated=@(rg --files -g '*.cs' -g '*.csproj' -g '!弃用资料/**' -g '!**/obj/**' -g '!**/bin/**' -g '!.agents/**' -g '!.codex/**'|ForEach-Object{$_ -replace '\\','/'})
if($LASTEXITCODE -ne 0){throw '源码枚举失败'}
$taskFixedPaths=@($taskProperties|ForEach-Object{$_.Name})
$taskNew=@($taskEnumerated|Where-Object{$_ -notin $taskFixedPaths})
$taskIgnored=@($taskFixedPaths|Where-Object{$_ -notin $taskEnumerated -and $_ -notin $taskMissing})
$taskProtection=[ordered]@{
    recorded_at=(Get-Date).ToString('o');stage='P09-S02B累计源码保护，原单位快照四行贯穿读取接续PG'
    fixed_snapshot_source_count=$taskProperties.Count;source_candidate_types_exited=$taskExited.Count
    expected_missing_count=$taskExpectedMissing.Count;missing=$taskMissing;changed_files=$taskChanged
    unexpected_production_changes=$taskUnexpectedProduction;unexpected_missing=$taskUnexpectedMissing;new_sources=$taskNew
    current_enumerated_sources=$taskEnumerated.Count;existing_fixed_snapshot_files=$taskExisting
    ignored_existing_unchanged_artifacts=$taskIgnored;database_run=$true;source_deletions_this_batch=0
}
$taskProtection|ConvertTo-Json -Depth 10|Set-Content -LiteralPath docs/postgresql-migration/任务结果/P09-S02B-源码保护证据.json -Encoding utf8
if($taskProperties.Count -ne 2390 -or $taskExited.Count -ne 73 -or $taskMissing.Count -ne 81 -or $taskUnexpectedProduction.Count -ne 0 -or $taskUnexpectedMissing.Count -ne 0 -or $taskNew.Count -ne 0){throw '源码保护不符合本批边界'}

$taskBlocked=@(rg --files SwiftExport.Infrastructure -g ExecutionEligibilityRepository.cs)[0]
if((Get-FileHash -LiteralPath $taskBlocked).Hash -ne '0DDAE30D90FE9F268665AF4394C96BE24473030936AE39EDB4B6192B7DAF9378'){throw '待确认原源发生变化'}




$taskProtection|Add-Member -NotePropertyName allowed_production_scope -NotePropertyValue @(
 @{path=$taskAudit.path;after_sha256=$taskAudit.after_sha256;scope='此前F01一行XML，当前未再变'},
 @{path=$taskFactory;after_sha256=$taskF02.factory_after_sha256;scope='此前F02源类退出/接口保持及两行XML，当前未再变'}
)
$taskProtection|Add-Member -NotePropertyName retained_mixed_source -NotePropertyValue @{path=$taskFactory;interface_retained='IDbConnectionFactory';counts_as_missing_file=$false}
$taskProtection|ConvertTo-Json -Depth 12|Set-Content -LiteralPath 'docs/postgresql-migration/任务结果/P09-S02B-源码保护证据.json' -Encoding utf8
$taskRecordPath='docs/postgresql-migration/任务结果/P09-S02B-物料单位原快照-文件证据.json'
$taskRecord=Get-Content $taskRecordPath -Raw|ConvertFrom-Json
foreach($taskFile in $taskRecord.files){$taskFile|Add-Member -NotePropertyName after_sha256 -NotePropertyValue (Get-FileHash -LiteralPath $taskFile.path).Hash}
$taskTrx='TestResults/postgresql-migration/p09-s02b-demand-unit-pg.trx'
$taskData='TestResults/postgresql-migration/p09-s02b-demand-unit-data-protection.json'
[xml]$taskXml=Get-Content $taskTrx -Raw
$taskCounters=$taskXml.TestRun.ResultSummary.Counters
if([int]$taskCounters.total -ne 1 -or [int]$taskCounters.passed -ne 1 -or [int]$taskCounters.failed -ne 0){throw '本批实际TRX不符'}
$taskDataProof=Get-Content $taskData -Raw|ConvertFrom-Json
$taskOldRows=($taskDataProof.Tables|Measure-Object OldCount -Sum).Sum
$taskAdded=($taskDataProof.Tables|ForEach-Object{$_.AddedIds.Count}|Measure-Object -Sum).Sum
if($taskDataProof.Tables.Count -ne 50 -or $taskOldRows -ne 53012 -or $taskAdded -ne 8 -or @($taskDataProof.Tables|Where-Object{$_.Before -ne $_.After}).Count -ne 0 -or $taskDataProof.SequenceBefore -ne 96 -or $taskDataProof.SequenceAfter -ne 96){throw '本批数据保护不符'}
$taskFixtureConsumers=@(rg -l -w 'IndependentProcurementSqlFixture' SwiftExport.Application.Tests --glob '*.cs')
if($taskFixtureConsumers.Count -ne 13){throw '原公共夹具消费者数量不符'}
$taskAssembly='SwiftExport.Application.Tests/bin/Debug/net10.0/SwiftExport.Application.Tests.dll'
if((Get-Item -LiteralPath $taskAssembly).LastWriteTime -ge [datetime]$taskXml.TestRun.Times.start){throw '新程序集未在测试开始前完成'}
$taskRecord.status='本批验证通过：原四单位持久/当前产品变化/列表追踪候选及未知复核接续PG；公共LocalDB及S整体未退出'
$taskRecord|Add-Member -NotePropertyName completed_at -NotePropertyValue (Get-Date).ToString('o')
$taskRecord|Add-Member -NotePropertyName execution -NotePropertyValue @{
 build_command='dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -v minimal';build_exit=0;errors=0;existing_warnings=7;seconds=13.56;build_session=61578;build_session_terminal=$true
 test_command="& './docs/postgresql-migration/任务结果/P09-S02B-物料单位原快照-实际命令.ps1'";test_exit=0;total=1;passed=1;failed=0;skipped=0;method_milliseconds=[TimeSpan]::Parse([string]$taskXml.TestRun.Results.UnitTestResult.duration).TotalMilliseconds;host_seconds='约2秒含50旧表保护'
 artifacts=@($taskTrx,$taskData)|ForEach-Object{@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash}}
 compiled_assembly=@{path=$taskAssembly;sha256=(Get-FileHash -LiteralPath $taskAssembly).Hash;last_write=(Get-Item $taskAssembly).LastWriteTime.ToString('o');test_start=$taskXml.TestRun.Times.start;fresh_before_test=$true}
 database='192.168.3.12:5432/dv_next_pg_failure_tests_20261004';protected_tables=50;old_rows=53012;old_rows_unchanged=$true;added_rows=8;current_rows=53020;sequence_before=96;sequence_after=96
 added_tables=@($taskDataProof.Tables|Where-Object{$_.AddedIds.Count -gt 0}|ForEach-Object{@{table=$_.Table;count=$_.AddedIds.Count;uids=$_.AddedIds}})
 old_source_types_exited=$taskExited.Count;old_source_files_exited=$taskMissing.Count;changed_tests=@($taskChanged|Where-Object{$_ -like 'SwiftExport.Application.Tests/*'}).Count;source_protection='P09-S02B-源码保护证据.json';unexpected_production_changes=$taskUnexpectedProduction.Count;unexpected_missing=$taskUnexpectedMissing.Count;new_sources=$taskNew.Count
 other_clients_after=0;advisory_locks_after=0;test_processes_after=@(Get-Process -Name testhost,vstest.console -ErrorAction SilentlyContinue).Count
 database_gates=12;integration_categories=36;ddl=$false;runtime_or_backup_write=$false;account_or_permission_change=$false;business_or_pg_source_change=$false;shared_fixture_change=$false;full_test_run=$false;new_test_matrix=$false;repeated_green_run=$false;commit_merge_push=$false
 local_command_issues='数次只读路径/输出范围定位已修正，无副作用。构建工具首返live session61578，随后同一handle核对完成exit0；测试实际加载新类，新程序集11:18:29.3598258早于测试11:18:49.2595689，原场景首次1通过，不把空选择/旧程序集或未知构建终态标通过、不重启或重跑。'
}
$taskRecord|Add-Member -NotePropertyName remaining_consumers -NotePropertyValue @{localdb_fixture=$taskFixtureConsumers;sql_baseline=@(rg -l 'SwiftExport\.Schema\.sql' SwiftExport.Application.Tests _tools --glob '*.cs');sql_generator=@(rg -l -w 'SqlServerSchemaGenerator' SwiftExport.Application.Tests _tools SwiftExport.Infrastructure --glob '*.cs')}
$taskRecord|ConvertTo-Json -Depth 18|Set-Content -LiteralPath $taskRecordPath -Encoding utf8
[pscustomobject]@{OriginalSources=$taskProperties.Count;ExitedTypes=$taskExited.Count;ExitedFiles=$taskMissing.Count;ChangedTests=$taskRecord.execution.changed_tests;UnexpectedProduction=$taskUnexpectedProduction.Count;UnexpectedMissing=$taskUnexpectedMissing.Count;NewSources=$taskNew.Count;Passed=1;OriginalRows=53012;AddedRows=8;FixtureConsumers=$taskFixtureConsumers.Count;TestProcesses=$taskRecord.execution.test_processes_after}|Format-List
