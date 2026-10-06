# P09-S02C：原共享收货接PG；核对2390原源和此前F01/F02精准范围、本批数据与新程序集，复用通过事实，不连接库或重跑测试。
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
    recorded_at=(Get-Date).ToString('o');stage='P09-S02C累计源码保护，原共享收货/分页/反向/历史贯穿接续PG'
    fixed_snapshot_source_count=$taskProperties.Count;source_candidate_types_exited=$taskExited.Count
    expected_missing_count=$taskExpectedMissing.Count;missing=$taskMissing;changed_files=$taskChanged
    unexpected_production_changes=$taskUnexpectedProduction;unexpected_missing=$taskUnexpectedMissing;new_sources=$taskNew
    current_enumerated_sources=$taskEnumerated.Count;existing_fixed_snapshot_files=$taskExisting
    ignored_existing_unchanged_artifacts=$taskIgnored;database_run=$true;source_deletions_this_batch=0
}
$taskProtection|ConvertTo-Json -Depth 10|Set-Content -LiteralPath docs/postgresql-migration/任务结果/P09-S02C-源码保护证据.json -Encoding utf8
if($taskProperties.Count -ne 2390 -or $taskExited.Count -ne 73 -or $taskMissing.Count -ne 81 -or $taskUnexpectedProduction.Count -ne 0 -or $taskUnexpectedMissing.Count -ne 0 -or $taskNew.Count -ne 0){throw '源码保护不符合本批边界'}

$taskBlocked=@(rg --files SwiftExport.Infrastructure -g ExecutionEligibilityRepository.cs)[0]
if((Get-FileHash -LiteralPath $taskBlocked).Hash -ne '0DDAE30D90FE9F268665AF4394C96BE24473030936AE39EDB4B6192B7DAF9378'){throw '待确认原源发生变化'}




$taskProtection|Add-Member -NotePropertyName allowed_production_scope -NotePropertyValue @(
 @{path=$taskAudit.path;after_sha256=$taskAudit.after_sha256;scope='此前F01一行XML，当前未再变'},
 @{path=$taskFactory;after_sha256=$taskF02.factory_after_sha256;scope='此前F02源类退出/接口保持及两行XML，当前未再变'}
)
$taskProtection|Add-Member -NotePropertyName retained_mixed_source -NotePropertyValue @{path=$taskFactory;interface_retained='IDbConnectionFactory';counts_as_missing_file=$false}
$taskProtection|ConvertTo-Json -Depth 12|Set-Content -LiteralPath 'docs/postgresql-migration/任务结果/P09-S02C-源码保护证据.json' -Encoding utf8
$taskRecordPath='docs/postgresql-migration/任务结果/P09-S02C-物料原共享收货-文件证据.json'
$taskRecord=Get-Content $taskRecordPath -Raw|ConvertFrom-Json
foreach($taskFile in $taskRecord.files){$taskFile|Add-Member -NotePropertyName after_sha256 -NotePropertyValue (Get-FileHash -LiteralPath $taskFile.path).Hash}
$taskTrx='TestResults/postgresql-migration/p09-s02c-material-trace-pg.trx'
$taskData='TestResults/postgresql-migration/p09-s02c-material-trace-data-protection.json'
[xml]$taskXml=Get-Content $taskTrx -Raw
$taskCounters=$taskXml.TestRun.ResultSummary.Counters
if([int]$taskCounters.total -ne 1 -or [int]$taskCounters.passed -ne 1 -or [int]$taskCounters.failed -ne 0){throw '本批实际TRX不符'}
$taskDataProof=Get-Content $taskData -Raw|ConvertFrom-Json
$taskOldRows=($taskDataProof.Tables|Measure-Object OldCount -Sum).Sum
$taskAdded=($taskDataProof.Tables|ForEach-Object{$_.AddedIds.Count}|Measure-Object -Sum).Sum
if($taskDataProof.Tables.Count -ne 50 -or $taskOldRows -ne 53020 -or $taskAdded -ne 15 -or @($taskDataProof.Tables|Where-Object{$_.Before -ne $_.After}).Count -ne 0 -or $taskDataProof.SequenceBefore -ne 96 -or $taskDataProof.SequenceAfter -ne 96){throw '本批数据保护不符'}
$taskFixtureConsumers=@(rg -l -w 'IndependentProcurementSqlFixture' SwiftExport.Application.Tests --glob '*.cs')
if($taskFixtureConsumers.Count -ne 12){throw '原公共夹具消费者数量不符'}
$taskAssembly='SwiftExport.Application.Tests/bin/Debug/net10.0/SwiftExport.Application.Tests.dll'
if((Get-Item -LiteralPath $taskAssembly).LastWriteTime -ge [datetime]$taskXml.TestRun.Times.start){throw '新程序集未在测试开始前完成'}
$taskRecord|Add-Member -NotePropertyName retained_original_business_assertions -NotePropertyValue 18
$taskRecord.status='本批验证通过：原6+6/7共享覆盖/分页/反向异常及回退和缺指纹/单位复核接续PG；公共LocalDB及S整体未退出'
$taskRecord|Add-Member -NotePropertyName completed_at -NotePropertyValue (Get-Date).ToString('o')
$taskRecord|Add-Member -NotePropertyName execution -NotePropertyValue @{
 build_command='dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -v minimal';build_exit=0;errors=0;existing_warnings=7;seconds=7.32;build_session_terminal=$true
 test_command="& './docs/postgresql-migration/任务结果/P09-S02C-物料原共享收货-实际命令.ps1'";test_exit=0;total=1;passed=1;failed=0;skipped=0;method_milliseconds=[TimeSpan]::Parse([string]$taskXml.TestRun.Results.UnitTestResult.duration).TotalMilliseconds;host_seconds='约2秒含50旧表保护'
 artifacts=@($taskTrx,$taskData)|ForEach-Object{@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash}}
 compiled_assembly=@{path=$taskAssembly;sha256=(Get-FileHash -LiteralPath $taskAssembly).Hash;last_write=(Get-Item $taskAssembly).LastWriteTime.ToString('o');test_start=$taskXml.TestRun.Times.start;fresh_before_test=$true}
 database='192.168.3.12:5432/dv_next_pg_failure_tests_20261004';protected_tables=50;old_rows=53020;old_rows_unchanged=$true;added_rows=15;current_rows=53035;sequence_before=96;sequence_after=96
 added_tables=@($taskDataProof.Tables|Where-Object{$_.AddedIds.Count -gt 0}|ForEach-Object{@{table=$_.Table;count=$_.AddedIds.Count;uids=$_.AddedIds}})
 old_source_types_exited=$taskExited.Count;old_source_files_exited=$taskMissing.Count;changed_tests=@($taskChanged|Where-Object{$_ -like 'SwiftExport.Application.Tests/*'}).Count;source_protection='P09-S02C-源码保护证据.json';unexpected_production_changes=$taskUnexpectedProduction.Count;unexpected_missing=$taskUnexpectedMissing.Count;new_sources=$taskNew.Count
 other_clients_after=1;other_client_state='idle，无事务';other_active_transactions_after=0;advisory_locks_after=0;test_processes_after=@(Get-Process -Name testhost,vstest.console -ErrorAction SilentlyContinue).Count
 database_gates=12;pg_integration_category_files=42;pg_integration_attribute_occurrences=54;ddl=$false;runtime_or_backup_write=$false;account_or_permission_change=$false;business_or_pg_source_change=$false;shared_fixture_change=$false;full_test_run=$false;new_test_matrix=$false;repeated_green_run=$false;commit_merge_push=$false
 local_command_issues='数次只读定位与DBX输出上限已收敛。收口脚本字符串替换误将81改151导致检查失败，实际81原缺失不变；仅修正记录守卫，无源或数据库修改。源码断言守卫首包含旧LocalDB门禁Inconclusive导致记录检查报缺失；排除这一被新PG门禁替换的环境检查后18条原业务断言全部逐字保留，无源修改。构建已确认terminal exit0后才运行原方法；无编译/测试失败，不重跑绿色。'
}
$taskRecord|Add-Member -NotePropertyName remaining_consumers -NotePropertyValue @{localdb_fixture=$taskFixtureConsumers;sql_baseline=@(rg -l 'SwiftExport\.Schema\.sql' SwiftExport.Application.Tests _tools --glob '*.cs');sql_generator=@(rg -l -w 'SqlServerSchemaGenerator' SwiftExport.Application.Tests _tools SwiftExport.Infrastructure --glob '*.cs')}
$taskRecord|ConvertTo-Json -Depth 18|Set-Content -LiteralPath $taskRecordPath -Encoding utf8
[pscustomobject]@{OriginalSources=$taskProperties.Count;ExitedTypes=$taskExited.Count;ExitedFiles=$taskMissing.Count;ChangedTests=$taskRecord.execution.changed_tests;UnexpectedProduction=$taskUnexpectedProduction.Count;UnexpectedMissing=$taskUnexpectedMissing.Count;NewSources=$taskNew.Count;Passed=1;OriginalRows=53020;AddedRows=15;FixtureConsumers=$taskFixtureConsumers.Count;TestProcesses=$taskRecord.execution.test_processes_after}|Format-List



