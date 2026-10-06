# P09-S02D：原来源追踪接PG；核对2390原源和此前F01/F02精准范围、本批数据与新程序集，复用通过事实，不连接库或重跑测试。
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
        if($taskPath -notlike 'SwiftExport.Application.Tests/*' -and $taskPath -notin @('_tools/DbSeed/DbSeed.csproj','_tools/AdminPasswordRecovery/AdminPasswordRecovery.csproj','SwiftExport.Infrastructure/Services/Auth/AuditStampService.cs','SwiftExport.Infrastructure/Data/DbConnectionFactory.cs','_tools/SchemaGenerator/Program.cs')){$taskUnexpectedProduction+=$taskPath}
    }
}
$taskEnumerated=@(rg --files -g '*.cs' -g '*.csproj' -g '!弃用资料/**' -g '!**/obj/**' -g '!**/bin/**' -g '!.agents/**' -g '!.codex/**'|ForEach-Object{$_ -replace '\\','/'})
if($LASTEXITCODE -ne 0){throw '源码枚举失败'}
$taskFixedPaths=@($taskProperties|ForEach-Object{$_.Name})
$taskNew=@($taskEnumerated|Where-Object{$_ -notin $taskFixedPaths})
$taskIgnored=@($taskFixedPaths|Where-Object{$_ -notin $taskEnumerated -and $_ -notin $taskMissing})
$taskProtection=[ordered]@{
    recorded_at=(Get-Date).ToString('o');stage='P09-S02D累计源码保护，原来源采购交付追踪两个方法迁PG'
    fixed_snapshot_source_count=$taskProperties.Count;source_candidate_types_exited=$taskExited.Count
    expected_missing_count=$taskExpectedMissing.Count;missing=$taskMissing;changed_files=$taskChanged
    unexpected_production_changes=$taskUnexpectedProduction;unexpected_missing=$taskUnexpectedMissing;new_sources=$taskNew
    current_enumerated_sources=$taskEnumerated.Count;existing_fixed_snapshot_files=$taskExisting
    ignored_existing_unchanged_artifacts=$taskIgnored;database_run=$true;source_deletions_this_batch=0
}
$taskProtection|ConvertTo-Json -Depth 10|Set-Content -LiteralPath docs/postgresql-migration/任务结果/P09-S02D-源码保护证据.json -Encoding utf8
if($taskProperties.Count -ne 2390 -or $taskExited.Count -ne 74 -or $taskMissing.Count -ne 82 -or $taskUnexpectedProduction.Count -ne 0 -or $taskUnexpectedMissing.Count -ne 0 -or $taskNew.Count -ne 0){throw '源码保护不符合本批边界'}

$taskBlocked=@(rg --files SwiftExport.Infrastructure -g ExecutionEligibilityRepository.cs)[0]
if((Get-FileHash -LiteralPath $taskBlocked).Hash -ne '0DDAE30D90FE9F268665AF4394C96BE24473030936AE39EDB4B6192B7DAF9378'){throw '待确认原源发生变化'}

$taskS04=Get-Content 'docs/postgresql-migration/任务结果/P09-S04-旧结构生成分支-文件证据.json' -Raw|ConvertFrom-Json
$taskTool=@($taskS04.files|Where-Object{$_.path -eq '_tools/SchemaGenerator/Program.cs'})[0]
if((Get-FileHash -LiteralPath $taskTool.path).Hash -ne $taskTool.after_sha256){throw '此前S04工具精准范围变化'}
$taskProtection|Add-Member -NotePropertyName allowed_production_scope -NotePropertyValue @(
 @{path=$taskAudit.path;after_sha256=$taskAudit.after_sha256;scope='此前F01审计XML精准范围保持'},
 @{path=$taskFactory;after_sha256=$taskF02.factory_after_sha256;scope='此前F02接口保留/旧类退出精准范围保持'},
 @{path=$taskTool.path;after_sha256=$taskTool.after_sha256;scope='此前S04只PG工具精准范围保持'}
)
$taskProtection|ConvertTo-Json -Depth 12|Set-Content -LiteralPath 'docs/postgresql-migration/任务结果/P09-S02D-源码保护证据.json' -Encoding utf8
$taskRecordPath='docs/postgresql-migration/任务结果/P09-S02D-原来源采购交付追踪-文件证据.json'
$taskRecord=Get-Content $taskRecordPath -Raw|ConvertFrom-Json
$taskSourcePath=$taskRecord.files[0].path
$taskText=Get-Content $taskSourcePath -Raw
$taskAssertions=@([regex]::Matches($taskRecord.files[0].before_text,'(?m)^\s*(?:Assert|StringAssert)\..*$')|ForEach-Object{$_.Value.Trim()}|Where-Object{$_ -notmatch '^Assert\.Inconclusive\('})
if($taskAssertions.Count -ne 47 -or @($taskAssertions|Where-Object{-not $taskText.Contains($_)}).Count -ne 0){throw '原47业务断言缺失'}
# 恢复第一方法两整数为首次误译TRUE，完整字节SHA必须复现首测源码；证明绿色第二方法与共用夹具没有被改。
$taskReconstruct=$taskText.Replace('@fingerprint,1,1,0,@root,NULL','@fingerprint,TRUE,1,0,@root,NULL').Replace('@fingerprint,1,2,0,@root,@root','@fingerprint,TRUE,2,0,@root,@root')
$taskFirstSourceSHA=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($taskReconstruct)))
if($taskFirstSourceSHA -ne '8043DDA225DAB6E19D6766E8A75EDF1853CF0942CB2B8563C81D6DD31DB2648E'){throw '首测之后存在超出两整数修正的源码变化'}
$taskArtifacts=@('TestResults/postgresql-migration/p09-s02d-gross-trace-pg-first.trx','TestResults/postgresql-migration/p09-s02d-gross-trace-data-protection-first.json','TestResults/postgresql-migration/p09-s02d-gross-trace-pg-retry.trx','TestResults/postgresql-migration/p09-s02d-gross-trace-data-protection-retry.json')
[xml]$taskFirstTrx=Get-Content $taskArtifacts[0] -Raw
[xml]$taskRetryTrx=Get-Content $taskArtifacts[2] -Raw
if([int]$taskFirstTrx.TestRun.ResultSummary.Counters.total -ne 2 -or [int]$taskFirstTrx.TestRun.ResultSummary.Counters.passed -ne 1 -or [int]$taskFirstTrx.TestRun.ResultSummary.Counters.failed -ne 1 -or [int]$taskRetryTrx.TestRun.ResultSummary.Counters.total -ne 1 -or [int]$taskRetryTrx.TestRun.ResultSummary.Counters.passed -ne 1 -or [int]$taskRetryTrx.TestRun.ResultSummary.Counters.failed -ne 0){throw '真实TRX结果不符'}
$taskFirstData=Get-Content $taskArtifacts[1] -Raw|ConvertFrom-Json
$taskRetryData=Get-Content $taskArtifacts[3] -Raw|ConvertFrom-Json
$taskStages=@(@{data=$taskFirstData;old=53035;added=13},@{data=$taskRetryData;old=53048;added=18})
foreach($taskStage in $taskStages){
 $taskProof=$taskStage.data
 if($taskProof.Tables.Count -ne 50 -or ($taskProof.Tables|Measure-Object OldCount -Sum).Sum -ne $taskStage.old -or ($taskProof.Tables|ForEach-Object{$_.AddedIds.Count}|Measure-Object -Sum).Sum -ne $taskStage.added -or @($taskProof.Tables|Where-Object{$_.Before -ne $_.After}).Count -ne 0 -or $taskProof.SequenceBefore -ne 96 -or $taskProof.SequenceAfter -ne 96){throw '两阶段原数据保护不符'}
}
$taskFixtureConsumers=@(rg -l -w 'IndependentProcurementSqlFixture' SwiftExport.Application.Tests --glob '*.cs')
if($taskFixtureConsumers.Count -ne 11 -or @($taskChanged|Where-Object{$_ -like 'SwiftExport.Application.Tests/*'}).Count -ne 53){throw '当前源码消费者/测试范围不符'}
$taskAssembly='SwiftExport.Application.Tests/bin/Debug/net10.0/SwiftExport.Application.Tests.dll'
if((Get-Item -LiteralPath $taskAssembly).LastWriteTime -ge [datetime]$taskRetryTrx.TestRun.Times.start){throw '修正程序集未在重验前生成'}
$taskRecord.files[0]|Add-Member -NotePropertyName after_sha256 -NotePropertyValue (Get-FileHash -LiteralPath $taskSourcePath).Hash
$taskRecord.status='验证通过：原两方法47断言保持，第二方法首次通过/第一方法夹具两整数修正后通过；其他S仍待'
$taskRecord|Add-Member -NotePropertyName completed_at -NotePropertyValue (Get-Date).ToString('o')
$taskRecord|Add-Member -NotePropertyName execution -NotePropertyValue @{
 build_command='dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -v minimal';build_exits=@(0,0);errors=0;existing_warnings=7;seconds=@(5.08,3.23)
 retry_command="& './docs/postgresql-migration/任务结果/P09-S02D-原来源采购交付追踪-夹具修正实际命令.ps1'";retry_exit=0;retry_passed=1;retry_failed=0;retry_milliseconds=[TimeSpan]::Parse([string]$taskRetryTrx.TestRun.Results.UnitTestResult.duration).TotalMilliseconds
 validated_original_methods=2;retained_original_business_assertions=47;green_delivery_method_repeated=$false;first_source_sha256=$taskFirstSourceSHA;only_first_method_two_integer_values_changed_since_first=$true
 database='192.168.3.12:5432/dv_next_pg_failure_tests_20261004';tables=50;old_rows=53035;old_rows_unchanged_both_stages=$true;added_rows_first=13;added_rows_retry=18;added_rows_total=31;current_rows=53066;inventory_sequence_before=96;inventory_sequence_after=96
 added_tables=@($taskStages|ForEach-Object{@{prior_rows=$_.old;added=@($_.data.Tables|Where-Object{$_.AddedIds.Count -gt 0}|ForEach-Object{@{table=$_.Table;uids=$_.AddedIds;count=$_.AddedIds.Count}})}})
 artifacts=@($taskArtifacts|ForEach-Object{@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash}})
 source_protection='P09-S02D-源码保护证据.json';original_sources=2390;source_types_exited=74;source_files_exited=82;changed_existing_tests=53;unexpected_production_changes=0;unexpected_missing=0;new_sources=0
 localdb_fixture_consumers_including_definition=11;other_clients_after=0;other_active_transactions_after=0;advisory_locks_after=0;test_processes_after=@(Get-Process -Name testhost,vstest.console -ErrorAction SilentlyContinue).Count
 compiled_assembly=@{path=$taskAssembly;sha256=(Get-FileHash -LiteralPath $taskAssembly).Hash;last_write=(Get-Item -LiteralPath $taskAssembly).LastWriteTime.ToString('o');retry_start=$taskRetryTrx.TestRun.Times.start;fresh_before_retry=$true}
 database_gates=12;pg_integration_category_files=@(rg -l 'TestCategory\("PostgreSqlIntegration"\)' SwiftExport.Application.Tests --glob '*.cs').Count
 ddl=$false;runtime_or_backup_write=$false;business_or_pg_source_change=$false;shared_fixture_change=$false;account_or_permission_change=$false;new_test_matrix=$false;full_test_run=$false;commit_merge_push=$false
}
$taskRecord|Add-Member -NotePropertyName remaining_consumers -NotePropertyValue @{localdb_fixture=$taskFixtureConsumers;sql_baseline=@(rg -l 'SwiftExport\.Schema\.sql' SwiftExport.Application.Tests _tools --glob '*.cs')}
$taskRecord|ConvertTo-Json -Depth 18|Set-Content -LiteralPath $taskRecordPath -Encoding utf8
[pscustomobject]@{OriginalSources=2390;ExitedTypes=74;ExitedFiles=82;ChangedTests=53;UnexpectedProduction=$taskUnexpectedProduction.Count;UnexpectedMissing=$taskUnexpectedMissing.Count;NewSources=$taskNew.Count;OriginalMethodsValidated=2;RetainedAssertions=47;SourceReconstruct=$true;FirstAdded=13;RetryAdded=18;CurrentRows=53066;FixtureConsumers=11;TestProcesses=$taskRecord.execution.test_processes_after}|Format-List


