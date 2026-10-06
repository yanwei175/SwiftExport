# P09-S01：只核对2390原源快照、五既有测试变化及实测证据；不连接数据库、不重跑已通过测试。
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
    recorded_at=(Get-Date).ToString('o');stage='P09-S01累计源码保护，五份现有结构契约转PG'
    fixed_snapshot_source_count=$taskProperties.Count;source_candidate_types_exited=$taskExited.Count
    expected_missing_count=$taskExpectedMissing.Count;missing=$taskMissing;changed_files=$taskChanged
    unexpected_production_changes=$taskUnexpectedProduction;unexpected_missing=$taskUnexpectedMissing;new_sources=$taskNew
    current_enumerated_sources=$taskEnumerated.Count;existing_fixed_snapshot_files=$taskExisting
    ignored_existing_unchanged_artifacts=$taskIgnored;database_run=$false;source_deletions_this_batch=0
}
$taskProtection|ConvertTo-Json -Depth 10|Set-Content -LiteralPath docs/postgresql-migration/任务结果/P09-S01-源码保护证据.json -Encoding utf8
if($taskProperties.Count -ne 2390 -or $taskExited.Count -ne 70 -or $taskMissing.Count -ne 79 -or $taskUnexpectedProduction.Count -ne 0 -or $taskUnexpectedMissing.Count -ne 0 -or $taskNew.Count -ne 0){throw '源码保护不符合本批边界'}

$taskBlocked=@(rg --files SwiftExport.Infrastructure -g ExecutionEligibilityRepository.cs)[0]
if((Get-FileHash -LiteralPath $taskBlocked).Hash -ne '0DDAE30D90FE9F268665AF4394C96BE24473030936AE39EDB4B6192B7DAF9378'){throw '待确认原源发生变化'}
$taskRecordPath='docs/postgresql-migration/任务结果/P09-S01-PG结构契约-文件证据.json'
$taskRecord=Get-Content $taskRecordPath -Raw|ConvertFrom-Json
foreach($taskFile in $taskRecord.files){$taskFile|Add-Member -NotePropertyName after_sha256 -NotePropertyValue (Get-FileHash -LiteralPath $taskFile.path).Hash}
$taskTrx='TestResults/postgresql-migration/p09-s01-schema-contracts-offline.trx'
[xml]$taskXml=Get-Content -LiteralPath $taskTrx -Raw
$taskCounters=$taskXml.TestRun.ResultSummary.Counters
if([int]$taskCounters.total -ne 8 -or [int]$taskCounters.passed -ne 8 -or [int]$taskCounters.failed -ne 0){throw '实际TRX不匹配八方法验收'}
$taskRecord.status='代码完成并限定验证通过：五现有结构契约八方法转PG；S源生成器/旧LocalDB有效消费者及旧脚本未退出'
$taskRecord|Add-Member -NotePropertyName completed_at -NotePropertyValue (Get-Date).ToString('o')
$taskRecord|Add-Member -NotePropertyName execution -NotePropertyValue @{
 build_command='dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -v minimal';build_exit=0;build_errors=0;existing_warnings=7;build_seconds=5.31
 test_command="& './docs/postgresql-migration/任务结果/P09-S01-PG结构契约-实际命令.ps1'";test_exit=0;test_total=8;test_passed=8;test_failed=0;test_skipped=0;test_milliseconds=108
 trx=@{path=$taskTrx;sha256=(Get-FileHash -LiteralPath $taskTrx).Hash};test_names=@($taskXml.TestRun.Results.UnitTestResult|ForEach-Object{$_.testName})
 source_protection='P09-S01-源码保护证据.json';unexpected_production_changes=$taskUnexpectedProduction.Count;unexpected_missing=$taskUnexpectedMissing.Count;new_sources=$taskNew.Count
 changed_tests=@($taskChanged|Where-Object{$_ -like 'SwiftExport.Application.Tests/*'}).Count;source_types_exited=$taskExited.Count;source_files_exited=$taskMissing.Count
 current_database_gates=12;integration_categories=36;database_connection=$false;ddl=$false;full_test_run=$false;business_source_change=$false;commit_merge_push=$false
 test_processes_after=@(Get-Process -Name testhost,vstest.console -ErrorAction SilentlyContinue).Count
 local_command_issues='数个只读历史报告/脚本路径猜测不存在，随后用rg发现；无文件/数据库写入。登记时多计一方法，运行前按实际八方法纠正，未凑数或扩大测试。'
}
$taskRecord|Add-Member -NotePropertyName remaining_consumers -NotePropertyValue @{
 sql_generator=@(rg -l -w 'SqlServerSchemaGenerator' SwiftExport.Application.Tests _tools SwiftExport.Infrastructure --glob '*.cs')
 sql_baseline=@(rg -l 'SwiftExport\.Schema\.sql' SwiftExport.Application.Tests _tools --glob '*.cs')
 localdb_fixture=@(rg -l -w 'IndependentProcurementSqlFixture' SwiftExport.Application.Tests --glob '*.cs')
 sqlclient_sources=@(rg -l 'using Microsoft\.Data\.SqlClient' SwiftExport.Application.Tests SwiftExport.Infrastructure _tools --glob '*.cs')
}
$taskRecord|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $taskRecordPath -Encoding utf8
$taskEntry=$taskManifest|Where-Object{$_.name -eq 'SqlServerSchemaGenerator'}
$taskEntry.status='开发中：P09-S01五份结构契约八方法转PG限定通过；旧工具及LocalDB有效消费者/旧脚本待退出'
$taskEntry.verification+='；S01五份当前结构契约迁PG，确定性/中文表列注释总数/完整来源身份/主产品约束/库存不可变与反向唯一/箱单数量创建幂等保留；八离线108ms和必要编译0错误7旧警告5.31秒通过，无库或生产源变化。旧生成器仍由工具消费，旧基线由LocalDB夹具及目录断言消费；源生成器未退出。'
$taskManifest|ConvertTo-Json -Depth 15|Set-Content -LiteralPath $taskManifestPath -Encoding utf8
[pscustomobject]@{OriginalSources=$taskProperties.Count;ExitedTypes=$taskExited.Count;ExitedFiles=$taskMissing.Count;ChangedTests=$taskRecord.execution.changed_tests;UnexpectedProduction=$taskUnexpectedProduction.Count;UnexpectedMissing=$taskUnexpectedMissing.Count;NewSources=$taskNew.Count;Tests=8;Passed=8;TestProcesses=$taskRecord.execution.test_processes_after;RemainingLocalDbConsumerFiles=$taskRecord.remaining_consumers.localdb_fixture.Count}|Format-List
