# P09-S02E：原采购跟踪接PG；核对2390原源和此前F01/F02精准范围、本批数据与新程序集，复用通过事实，不连接库或重跑测试。
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
    recorded_at=(Get-Date).ToString('o');stage='P09-S02E累计源码保护，用户已批准四库，原四方法真实PG通过及当前HEAD干净旧源退出'
    fixed_snapshot_source_count=$taskProperties.Count;source_candidate_types_exited=$taskExited.Count
    expected_missing_count=$taskExpectedMissing.Count;missing=$taskMissing;changed_files=$taskChanged
    unexpected_production_changes=$taskUnexpectedProduction;unexpected_missing=$taskUnexpectedMissing;new_sources=$taskNew
    current_enumerated_sources=$taskEnumerated.Count;existing_fixed_snapshot_files=$taskExisting
    ignored_existing_unchanged_artifacts=$taskIgnored;database_run=$true;source_deletions_this_batch=0
}
$taskProtection|ConvertTo-Json -Depth 10|Set-Content -LiteralPath docs/postgresql-migration/任务结果/P09-S02E-四库收口-源码保护证据.json -Encoding utf8
if($taskProperties.Count -ne 2390 -or $taskExited.Count -ne 75 -or $taskMissing.Count -ne 83 -or $taskUnexpectedProduction.Count -ne 0 -or $taskUnexpectedMissing.Count -ne 0 -or $taskNew.Count -ne 0){throw '源码保护不符合本批边界'}

$taskR11Exit=Get-Content docs/postgresql-migration/任务结果/P09-R11-当前HEAD源退出-证据.json -Raw|ConvertFrom-Json
if(-not $taskR11Exit.removed -or -not $taskR11Exit.tracked_head_clean -or (Test-Path -LiteralPath $taskR11Exit.path)){throw '当前HEAD干净旧源退出事实不符'}
$taskR11Original=Get-Content docs/postgresql-migration/任务结果/P09-R11-执行资格源退出-文件证据.json -Raw|ConvertFrom-Json
$taskR11Bytes=[Convert]::FromBase64String($taskR11Original.files[0].prior_exact_bytes_base64)
if([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($taskR11Bytes)) -ne '0DDAE30D90FE9F268665AF4394C96BE24473030936AE39EDB4B6192B7DAF9378'){throw '旧源原字节保护变化'}

$taskS04=Get-Content 'docs/postgresql-migration/任务结果/P09-S04-旧结构生成分支-文件证据.json' -Raw|ConvertFrom-Json
$taskTool=@($taskS04.files|Where-Object{$_.path -eq '_tools/SchemaGenerator/Program.cs'})[0]
if((Get-FileHash -LiteralPath $taskTool.path).Hash -ne $taskTool.after_sha256){throw '此前S04工具精准范围变化'}
$taskProtection|Add-Member -NotePropertyName allowed_production_scope -NotePropertyValue @(
 @{path=$taskAudit.path;after_sha256=$taskAudit.after_sha256;scope='此前F01精准范围保持'},
 @{path=$taskFactory;after_sha256=$taskF02.factory_after_sha256;scope='此前F02精准范围保持'},
 @{path=$taskTool.path;after_sha256=$taskTool.after_sha256;scope='此前S04精准范围保持'}
)
$taskProtection|ConvertTo-Json -Depth 12|Set-Content -LiteralPath 'docs/postgresql-migration/任务结果/P09-S02E-四库收口-源码保护证据.json' -Encoding utf8

$taskRecordPath='docs/postgresql-migration/任务结果/P09-S02E-原采购整单跟踪-文件证据.json'
$taskRecord=Get-Content $taskRecordPath -Raw|ConvertFrom-Json
$taskSourcePath='SwiftExport.Application.Tests/Procurement/PurchaseOrderTrackingSqlTests.cs'
$taskCurrent=Get-Content $taskSourcePath -Raw
$taskOld=$taskRecord.pre_isolation_pg_source
$taskCurrentBody=$taskCurrent.Substring($taskCurrent.IndexOf('    public async Task UnlinkedOrderFilterRunsBeforeCountAndPaging()'))
$taskOldBody=$taskOld.Substring($taskOld.IndexOf('    public async Task UnlinkedOrderFilterRunsBeforeCountAndPaging()'))
$taskCurrentBody=($taskCurrentBody.Substring(0,$taskCurrentBody.IndexOf('    private static readonly Dictionary')) -replace "\r\n","\n")
$taskOldBody=($taskOldBody.Substring(0,$taskOldBody.IndexOf('    private static readonly Dictionary')) -replace "\r\n","\n")
if($taskCurrentBody -cne $taskOldBody){throw '四个原方法正文变化'}
$taskAssertions=@([regex]::Matches($taskOldBody,'(?m)^\s*(?:Assert|StringAssert|CollectionAssert)\..*$'))
if($taskAssertions.Count -ne 35){throw '原35业务断言不符'}
$taskTrxPath='TestResults/postgresql-migration/p09-s02e-purchase-tracking-isolated-pg.trx'
$taskDataPath='TestResults/postgresql-migration/p09-s02e-purchase-tracking-isolated-data-protection.json'
[xml]$taskTrx=Get-Content $taskTrxPath -Raw
$taskData=Get-Content $taskDataPath -Raw|ConvertFrom-Json
if([int]$taskTrx.TestRun.ResultSummary.Counters.total -ne 4 -or [int]$taskTrx.TestRun.ResultSummary.Counters.passed -ne 4 -or [int]$taskTrx.TestRun.ResultSummary.Counters.failed -ne 0){throw '四原方法实测未通过'}
if($taskData.Tables.Count -ne 50 -or ($taskData.Tables|Measure-Object OldCount -Sum).Sum -ne 53196 -or ($taskData.Tables|ForEach-Object{$_.AddedIds.Count}|Measure-Object -Sum).Sum -ne 0 -or @($taskData.Tables|Where-Object{$_.Before -ne $_.After}).Count -ne 0 -or $taskData.SequenceBefore -ne 96 -or $taskData.SequenceAfter -ne 96 -or $taskData.StructureBefore -ne $taskData.StructureAfter){throw '来源50表旧行/结构/序列保护不符'}
if($taskData.OwnedDatabases.Count -ne 4 -or (@($taskData.OwnedDatabases.Database|Sort-Object) -join '|') -ne (@($taskRecord.isolation_revision.exact_proposed_databases|Sort-Object) -join '|')){throw '新库范围不是批准四库'}
foreach($taskDatabase in $taskData.OwnedDatabases){
 if(-not $taskDatabase.BaselineCopyComplete -or $taskDatabase.Tables.Count -ne 50 -or ($taskDatabase.Tables|Measure-Object OldCount -Sum).Sum -ne 162 -or @($taskDatabase.Tables|Where-Object{$_.Before -ne $_.After}).Count -ne 0 -or $taskDatabase.StructureBefore -ne $taskDatabase.StructureAfter -or $taskDatabase.InventorySequence -ne '1:false'){throw '新库基线/结构/序列保护不符'}
}
$taskAdded=($taskData.OwnedDatabases|ForEach-Object{$_.Tables}|ForEach-Object{$_.AddedIds.Count}|Measure-Object -Sum).Sum
if($taskAdded -ne 93){throw '四原方法完整新增事实不是93'}
if((Get-FileHash $taskRecord.isolation_first_execution.trx).Hash -ne $taskRecord.isolation_first_execution.trx_sha256 -or (Get-FileHash $taskRecord.isolation_first_execution.data).Hash -ne $taskRecord.isolation_first_execution.data_sha256){throw '空新库首轮失败证据变化'}
foreach($taskArtifact in $taskRecord.first_execution.artifacts){if((Get-FileHash $taskArtifact.path).Hash -ne $taskArtifact.sha256){throw '共享库原失败证据变化'}}
$taskRecord.status='验证通过：获准四独立PG库原四方法；两阶段原失败留证，P09整体仍未完成'
$taskRecord.isolation_revision.approval.status='用户已批准且实际完成；此前拒绝未绕过，原拒绝补丁未应用，获准实施另留证'
$taskRecord|Add-Member -NotePropertyName isolation_final_execution -NotePropertyValue @{
 source_sha256=(Get-FileHash $taskSourcePath).Hash;build_command='dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -v minimal';build_exit=0;errors=0;existing_warnings=7;build_seconds=4.48
 test_command="& './docs/postgresql-migration/任务结果/P09-S02E-四个新测试库-实际命令.ps1'";test_exit=0;passed=4;failed=0;skipped=0;original_methods_body_unchanged=$true;original_business_assertions=35
 trx=$taskTrxPath;trx_sha256=(Get-FileHash $taskTrxPath).Hash;data=$taskDataPath;data_sha256=(Get-FileHash $taskDataPath).Hash
 methods=@($taskTrx.TestRun.Results.UnitTestResult|ForEach-Object{@{name=$_.testName;outcome=$_.outcome;duration=$_.duration}})
 source_old_rows=53196;source_added_rows=0;source_all_50_tables_unchanged=$true;source_full_structure_before=$taskData.StructureBefore;source_full_structure_after=$taskData.StructureAfter;source_inventory_sequence=96
 copied_rows_each=162;copied_rows_total=648;business_rows_total=93;new_total_rows=741;new_database_count=4
 semantic_structure=$taskData.SemanticStructure;column_comment_difference_recorded=$true;source_and_new_full_comments_separately_protected=$true
 new_databases=@($taskData.OwnedDatabases|ForEach-Object{@{database=$_.Database;copied_rows=162;added_rows=($_.Tables|ForEach-Object{$_.AddedIds.Count}|Measure-Object -Sum).Sum;full_structure_before=$_.StructureBefore;full_structure_after=$_.StructureAfter;inventory_sequence=$_.InventorySequence;tables=$_.Tables}})
}
$taskRecord|Add-Member -NotePropertyName isolation_closure -NotePropertyValue @{
 command="& './docs/postgresql-migration/任务结果/P09-S02E-四库与旧源收口-实际命令.ps1'";original_sources=2390;source_types_exited=75;source_files_exited=83;changed_existing_tests=55;extra_production_changes=$taskUnexpectedProduction.Count;extra_missing=$taskUnexpectedMissing.Count;new_sources=$taskNew.Count;source_proof='P09-S02E-四库收口-源码保护证据.json'
 database_gates=@(rg -o --no-filename 'GetEnvironmentVariable\("SWIFTEXPORT_RUN_[^"]+"' SwiftExport.Application.Tests|Sort-Object -Unique).Count
 remaining_localdb_consumers_including_definition=@(rg -l -w IndependentProcurementSqlFixture SwiftExport.Application.Tests --glob '*.cs').Count
 other_clients_after=0;other_active_transactions_after=0;advisory_locks_after=0;test_processes_after=@(Get-Process -Name testhost,vstest.console -ErrorAction SilentlyContinue).Count
 production_pg_behavior_change=$false;runtime_or_backup_write=$false;existing_failure_ddl_or_data_write=$false;new_db_reset_or_drop=$false;full_test_run=$false;commit_merge_push=$false;known_pending_authorizations=0
}
$taskRecord|ConvertTo-Json -Depth 20|Set-Content -LiteralPath $taskRecordPath -Encoding utf8
$taskR11Exit|Add-Member -NotePropertyName validation -NotePropertyValue @{command='dotnet build SwiftExport.Infrastructure/SwiftExport.Infrastructure.csproj --no-restore -v minimal';exit=0;errors=0;warnings=0;seconds=1.04;source_proof='P09-S02E-四库收口-源码保护证据.json';current_consumer_count=0;database_or_test_repeat=$false}
$taskR11Exit|ConvertTo-Json -Depth 10|Set-Content docs/postgresql-migration/任务结果/P09-R11-当前HEAD源退出-证据.json -Encoding utf8
$taskCandidate=@($taskManifest|Where-Object{$_.name -eq 'ExecutionEligibilityRepository'})[0]
$taskCandidate.status='验证通过：P09-R11当前HEAD干净无消费者旧源退出，Infra必要编译0错误0警告'
$taskManifest|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $taskManifestPath -Encoding utf8
[pscustomobject]@{OriginalSources=2390;ExitedTypes=75;ExitedFiles=83;ExtraProduction=$taskUnexpectedProduction.Count;ExtraMissing=$taskUnexpectedMissing.Count;NewSources=$taskNew.Count;OriginalMethods=4;Passed=4;OriginalBusinessAssertions=35;SourceOldRows=53196;SourceAdded=0;NewDatabases=4;CopiedRows=648;NewBusinessRows=93;KnownPendingAuthorizations=0;TestProcesses=$taskRecord.isolation_closure.test_processes_after}|Format-List
