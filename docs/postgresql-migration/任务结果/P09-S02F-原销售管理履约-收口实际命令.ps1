# P09-S02F：原销售管理履约接PG；核对2390原源和此前F01/F02精准范围、本批数据与新程序集，复用通过事实，不连接库或重跑测试。
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
    recorded_at=(Get-Date).ToString('o');stage='P09-S02F累计源码保护，原销售管理一个方法接续，单列夹具修正后真实PG通过'
    fixed_snapshot_source_count=$taskProperties.Count;source_candidate_types_exited=$taskExited.Count
    expected_missing_count=$taskExpectedMissing.Count;missing=$taskMissing;changed_files=$taskChanged
    unexpected_production_changes=$taskUnexpectedProduction;unexpected_missing=$taskUnexpectedMissing;new_sources=$taskNew
    current_enumerated_sources=$taskEnumerated.Count;existing_fixed_snapshot_files=$taskExisting
    ignored_existing_unchanged_artifacts=$taskIgnored;database_run=$true;source_deletions_this_batch=0
}
$taskProtection|ConvertTo-Json -Depth 10|Set-Content -LiteralPath docs/postgresql-migration/任务结果/P09-S02F-源码保护证据.json -Encoding utf8
if($taskProperties.Count -ne 2390 -or $taskExited.Count -ne 74 -or $taskMissing.Count -ne 82 -or $taskUnexpectedProduction.Count -ne 0 -or $taskUnexpectedMissing.Count -ne 0 -or $taskNew.Count -ne 0){throw '源码保护不符合本批边界'}

$taskBlocked=@(rg --files SwiftExport.Infrastructure -g ExecutionEligibilityRepository.cs)[0]
if((Get-FileHash -LiteralPath $taskBlocked).Hash -ne '0DDAE30D90FE9F268665AF4394C96BE24473030936AE39EDB4B6192B7DAF9378'){throw '待确认原源发生变化'}

$taskS04=Get-Content 'docs/postgresql-migration/任务结果/P09-S04-旧结构生成分支-文件证据.json' -Raw|ConvertFrom-Json
$taskTool=@($taskS04.files|Where-Object{$_.path -eq '_tools/SchemaGenerator/Program.cs'})[0]
if((Get-FileHash -LiteralPath $taskTool.path).Hash -ne $taskTool.after_sha256){throw '此前S04工具精准范围变化'}
$taskProtection|Add-Member -NotePropertyName allowed_production_scope -NotePropertyValue @(
 @{path=$taskAudit.path;after_sha256=$taskAudit.after_sha256;scope='此前F01精准范围保持'},
 @{path=$taskFactory;after_sha256=$taskF02.factory_after_sha256;scope='此前F02精准范围保持'},
 @{path=$taskTool.path;after_sha256=$taskTool.after_sha256;scope='此前S04精准范围保持'}
)
$taskProtection|ConvertTo-Json -Depth 12|Set-Content -LiteralPath 'docs/postgresql-migration/任务结果/P09-S02F-源码保护证据.json' -Encoding utf8

$taskRecordPath='docs/postgresql-migration/任务结果/P09-S02F-原销售管理履约-文件证据.json'
$taskRecord=Get-Content $taskRecordPath -Raw|ConvertFrom-Json
$taskText=Get-Content $taskRecord.file -Raw
$taskAssertions=@([regex]::Matches($taskRecord.before_source,'(?m)^\s*(?:Assert|StringAssert|CollectionAssert)\..*$')|ForEach-Object{$_.Value.Trim()}|Where-Object{$_ -notmatch '^Assert\.Inconclusive\('})
$taskCurrentAssertions=@([regex]::Matches($taskText,'(?m)^\s*(?:Assert|StringAssert|CollectionAssert)\..*$')|ForEach-Object{$_.Value.Trim()}|Select-Object -First 77)
if($taskAssertions.Count -ne 77 -or ($taskAssertions -join "`n") -cne ($taskCurrentAssertions -join "`n")){throw '原77条有效断言或其次序变化'}
$taskRawText=[Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Join-Path (Get-Location) $taskRecord.file)))
$taskColumn='purchase_order_line(uid,purchase_order_id,line_number,order_no,sku,unit_snapshot,quantity)'
if(([regex]::Matches($taskRawText,[regex]::Escape($taskColumn))).Count -ne 1){throw '本批列修正不是唯一已知位置'}
$taskFirstText=$taskRawText.Replace($taskColumn,'purchase_order_line(uid,purchase_order_id,line_number,order_no,sku,unit_snapshot,qty)')
$taskRecoveredFirstSha=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($taskFirstText)))
if($taskRecoveredFirstSha -ne $taskRecord.first_execution.source_sha256){throw '重测前发生了单列修正以外的源码变化'}
$taskTrxPath='TestResults/postgresql-migration/p09-s02f-sales-manager-pg.trx'
$taskDataPath='TestResults/postgresql-migration/p09-s02f-sales-manager-data-protection.json'
[xml]$taskTrx=Get-Content $taskTrxPath -Raw
$taskData=Get-Content $taskDataPath -Raw|ConvertFrom-Json
if([int]$taskTrx.TestRun.ResultSummary.Counters.total -ne 1 -or [int]$taskTrx.TestRun.ResultSummary.Counters.passed -ne 1 -or [int]$taskTrx.TestRun.ResultSummary.Counters.failed -ne 0){throw '本批唯一原方法没有实际通过'}
if($taskData.Tables.Count -ne 50 -or ($taskData.Tables|Measure-Object OldCount -Sum).Sum -ne 53164 -or ($taskData.Tables|ForEach-Object{$_.AddedIds.Count}|Measure-Object -Sum).Sum -ne 32 -or @($taskData.Tables|Where-Object{$_.Before -ne $_.After}).Count -ne 0 -or $taskData.SequenceBefore -ne 96 -or $taskData.SequenceAfter -ne 96){throw '本批全部旧数据保护不符'}
if((Get-FileHash -LiteralPath $taskRecord.first_execution.trx).Hash -ne $taskRecord.first_execution.trx_sha256 -or (Get-FileHash -LiteralPath $taskRecord.first_execution.data).Hash -ne $taskRecord.first_execution.data_sha256){throw '原失败记录变更'}
$taskFixtureConsumers=@(rg -l -w 'IndependentProcurementSqlFixture' SwiftExport.Application.Tests --glob '*.cs')
if($taskFixtureConsumers.Count -ne 9 -or @($taskChanged|Where-Object{$_ -like 'SwiftExport.Application.Tests/*'}).Count -ne 55){throw '当前消费者/源码边界不符'}
$taskRecord.status='验证通过：原一个方法真PG；首次夹具方言失败另留证，P09整体未完成'
$taskRecord|Add-Member -NotePropertyName final_execution -NotePropertyValue @{
 source_sha256=(Get-FileHash -LiteralPath $taskRecord.file).Hash; build_command='dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -v minimal'
 build_exit=0;errors=0;existing_warnings=7;build_seconds=5.42; test_command="& './docs/postgresql-migration/任务结果/P09-S02F-原销售管理履约-实际命令.ps1'"
 test_exit=0;passed=1;failed=0;skipped=0;duration=$taskTrx.TestRun.Results.UnitTestResult.duration
 counterfactual_first_sha256=$taskRecoveredFirstSha;only_failed_column_fixed=$true; original_business_assertions=77
 trx=$taskTrxPath;trx_sha256=(Get-FileHash -LiteralPath $taskTrxPath).Hash;data=$taskDataPath;data_sha256=(Get-FileHash -LiteralPath $taskDataPath).Hash
 old_rows=53164;added_rows=32;old_rows_unchanged=$true;current_rows=53196;inventory_sequence=96
}
$taskRecord|Add-Member -NotePropertyName closure -NotePropertyValue @{
 command="& './docs/postgresql-migration/任务结果/P09-S02F-原销售管理履约-收口实际命令.ps1'";original_sources=2390;source_types_exited=74;source_files_exited=82;changed_existing_tests=55
 extra_production_changes=$taskUnexpectedProduction.Count;extra_missing=$taskUnexpectedMissing.Count;new_sources=$taskNew.Count;source_proof='P09-S02F-源码保护证据.json'
 localdb_fixture_consumers_including_definition=9;database_gates=12;pg_integration_category_files=@(rg -l 'TestCategory\("PostgreSqlIntegration"\)' SwiftExport.Application.Tests --glob '*.cs').Count
 added_tables=@($taskData.Tables|Where-Object{$_.AddedIds.Count -gt 0}|ForEach-Object{@{table=$_.Table;uids=$_.AddedIds;count=$_.AddedIds.Count}})
 other_clients_after=0;other_active_transactions_after=0;advisory_locks_after=0;test_processes_after=@(Get-Process -Name testhost,vstest.console -ErrorAction SilentlyContinue).Count
 production_or_pg_change=$false;shared_fixture_change=$false;ddl_or_new_db=$false;existing_account_or_permission_change=$false;runtime_or_backup_write=$false;full_test_run=$false;commit_merge_push=$false
 s02e_still_pending_approval=$true;s02e_new_databases=0
}
$taskRecord|Add-Member -NotePropertyName remaining_consumers -NotePropertyValue @{localdb_fixture=$taskFixtureConsumers;sql_baseline=@(rg -l 'SwiftExport\.Schema\.sql' SwiftExport.Application.Tests _tools --glob '*.cs')}
$taskRecord|ConvertTo-Json -Depth 14|Set-Content -LiteralPath $taskRecordPath -Encoding utf8
[pscustomobject]@{OriginalSources=2390;ExitedTypes=74;ExitedFiles=82;ChangedTests=55;UnexpectedProduction=$taskUnexpectedProduction.Count;UnexpectedMissing=$taskUnexpectedMissing.Count;NewSources=$taskNew.Count;OriginalAssertions=77;Passed=1;Failed=0;OldRowsUnchanged=53164;AddedRows=32;CounterfactualFirstSha=$taskRecoveredFirstSha;LocalDbConsumers=9;TestProcesses=$taskRecord.closure.test_processes_after}|Format-List
