# P09-S02A：核对2390原源快照、原管理筛选迁PG及实测数据证据；不连接数据库、不重跑已通过测试。
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskManifestPath='docs/postgresql-migration/任务结果/P09-当前源退出文件清单.json'
$taskManifest=Get-Content $taskManifestPath -Raw|ConvertFrom-Json
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
    recorded_at=(Get-Date).ToString('o');stage='P09-S02A累计源码保护，原管理筛选消费者转PG'
    fixed_snapshot_source_count=$taskProperties.Count;source_candidate_types_exited=$taskExited.Count
    expected_missing_count=$taskExpectedMissing.Count;missing=$taskMissing;changed_files=$taskChanged
    unexpected_production_changes=$taskUnexpectedProduction;unexpected_missing=$taskUnexpectedMissing;new_sources=$taskNew
    current_enumerated_sources=$taskEnumerated.Count;existing_fixed_snapshot_files=$taskExisting
    ignored_existing_unchanged_artifacts=$taskIgnored;database_run=$true;source_deletions_this_batch=0
}
$taskProtection|ConvertTo-Json -Depth 10|Set-Content -LiteralPath docs/postgresql-migration/任务结果/P09-S02A-源码保护证据.json -Encoding utf8
if($taskProperties.Count -ne 2390 -or $taskExited.Count -ne 70 -or $taskMissing.Count -ne 79 -or $taskUnexpectedProduction.Count -ne 0 -or $taskUnexpectedMissing.Count -ne 0 -or $taskNew.Count -ne 0){throw '源码保护不符合本批边界'}

$taskBlocked=@(rg --files SwiftExport.Infrastructure -g ExecutionEligibilityRepository.cs)[0]
if((Get-FileHash -LiteralPath $taskBlocked).Hash -ne '0DDAE30D90FE9F268665AF4394C96BE24473030936AE39EDB4B6192B7DAF9378'){throw '待确认原源发生变化'}

$taskRecordPath='docs/postgresql-migration/任务结果/P09-S02A-采购管理原筛选-文件证据.json'
$taskRecord=Get-Content $taskRecordPath -Raw|ConvertFrom-Json
foreach($taskFile in $taskRecord.files){$taskFile|Add-Member -NotePropertyName after_sha256 -NotePropertyValue (Get-FileHash -LiteralPath $taskFile.path).Hash}
$taskTrx='TestResults/postgresql-migration/p09-s02a-manager-filter-pg.trx'
$taskData='TestResults/postgresql-migration/p09-s02a-manager-filter-data-protection.json'
[xml]$taskXml=Get-Content $taskTrx -Raw
$taskCounters=$taskXml.TestRun.ResultSummary.Counters
if([int]$taskCounters.total -ne 1 -or [int]$taskCounters.passed -ne 1 -or [int]$taskCounters.failed -ne 0){throw '本批TRX不符'}
$taskProtectionData=Get-Content $taskData -Raw|ConvertFrom-Json
$taskOldRows=($taskProtectionData.Tables|Measure-Object OldCount -Sum).Sum
$taskAdded=($taskProtectionData.Tables|ForEach-Object{$_.AddedIds.Count}|Measure-Object -Sum).Sum
if($taskProtectionData.Tables.Count -ne 50 -or $taskOldRows -ne 52998 -or $taskAdded -ne 14 -or @($taskProtectionData.Tables|Where-Object{$_.Before -ne $_.After}).Count -ne 0 -or $taskProtectionData.SequenceBefore -ne 96 -or $taskProtectionData.SequenceAfter -ne 96){throw '数据保护范围不符'}
$taskFixtureConsumers=@(rg -l -w 'IndependentProcurementSqlFixture' SwiftExport.Application.Tests --glob '*.cs')
if($taskFixtureConsumers.Count -ne 14){throw '原公共夹具消费数量不符'}
$taskRecord.status='本批代码完成/真实PG通过：原管理组合筛选消费者接续，旧共享夹具及S整体未退出'
$taskRecord|Add-Member -NotePropertyName completed_at -NotePropertyValue (Get-Date).ToString('o')
$taskRecord|Add-Member -NotePropertyName execution -NotePropertyValue @{
 build_command='dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -v minimal';build_exit=0;build_errors=0;existing_warnings=7;build_seconds=2.90
 test_command="& './docs/postgresql-migration/任务结果/P09-S02A-采购管理原筛选-实际命令.ps1'";test_exit=0;test_total=1;test_passed=1;test_failed=0;test_skipped=0;method_milliseconds=[TimeSpan]::Parse([string]$taskXml.TestRun.Results.UnitTestResult.duration).TotalMilliseconds;host_seconds='约2秒，含50表原数据保护'
 artifacts=@($taskTrx,$taskData)|ForEach-Object{@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash}}
 source_protection='P09-S02A-源码保护证据.json';changed_tests=@($taskChanged|Where-Object{$_ -like 'SwiftExport.Application.Tests/*'}).Count;unexpected_production_changes=$taskUnexpectedProduction.Count;unexpected_missing=$taskUnexpectedMissing.Count;new_sources=$taskNew.Count;source_types_exited=$taskExited.Count;source_files_exited=$taskMissing.Count
 database='192.168.3.12:5432/dv_next_pg_failure_tests_20261004';protected_tables=50;old_rows=52998;old_rows_unchanged=$true;retained_new_rows=14;current_rows=53012;sequence_before=96;sequence_after=96
 new_tables=@($taskProtectionData.Tables|Where-Object{$_.AddedIds.Count -gt 0}|ForEach-Object{@{table=$_.Table;count=$_.AddedIds.Count;uids=$_.AddedIds}})
 database_gates=12;integration_categories=36;other_clients_after=0;advisory_locks_after=0;test_processes_after=@(Get-Process -Name testhost,vstest.console -ErrorAction SilentlyContinue).Count
 full_test_run=$false;repeated_green_run=$false;runtime_or_backup_write=$false;ddl=$false;account_or_permission_change=$false;business_or_pg_source_change=$false;commit_merge_push=$false
 input_scope='原九个筛选和一次倒置拒绝保持。原两正常创建/真实权限/回执/审计全链；原标准产品/正数数量及价格保持。随机编码限定同样两单，随机独立未来年月不占用旧号段；参数化原最后100ns输入由Npgsql按timestamp(6)存储，避免SQL七位字面量舍入次日。实际非匹配收货供应商也为本批有效新身份，不以不存在身份替换。'
 local_command_issues='一处只读PowerShell foreach输出管道语法失败，随后修正；数个只读路径猜测和一次通配符路径无匹配已改用rg定位，未写库或源码；结束无测试进程查询exit1不是测试失败。'
}
$taskRecord|Add-Member -NotePropertyName remaining_consumers -NotePropertyValue @{localdb_fixture=$taskFixtureConsumers;sql_baseline=@(rg -l 'SwiftExport\.Schema\.sql' SwiftExport.Application.Tests _tools --glob '*.cs');sql_generator=@(rg -l -w 'SqlServerSchemaGenerator' SwiftExport.Application.Tests _tools SwiftExport.Infrastructure --glob '*.cs')}
$taskRecord|ConvertTo-Json -Depth 18|Set-Content -LiteralPath $taskRecordPath -Encoding utf8
[pscustomobject]@{OriginalSources=$taskProperties.Count;ExitedTypes=$taskExited.Count;ExitedFiles=$taskMissing.Count;ChangedTests=$taskRecord.execution.changed_tests;UnexpectedProduction=$taskUnexpectedProduction.Count;UnexpectedMissing=$taskUnexpectedMissing.Count;NewSources=$taskNew.Count;Passed=1;OriginalRows=52998;AddedRows=14;FixtureConsumers=$taskFixtureConsumers.Count;TestProcesses=$taskRecord.execution.test_processes_after}|Format-List
