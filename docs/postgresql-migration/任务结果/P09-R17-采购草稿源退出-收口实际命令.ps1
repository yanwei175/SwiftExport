# P09-R17：登记已经验证的单个旧源退出，核对原2390源码/项目快照；不重复测试、不连接数据库。
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskManifestPath='docs/postgresql-migration/任务结果/P09-当前源退出文件清单.json'
$taskManifest=Get-Content $taskManifestPath -Raw|ConvertFrom-Json
$taskEntry=$taskManifest|Where-Object name -eq 'PurchaseOrderDraftRepository'
if(Test-Path -LiteralPath $taskEntry.definitions[0].path){throw '旧采购草稿仓储仍存在'}
$taskEntry.status='验证通过：P09-R17源文件已退出，原追加2真PG/3离线/必要构建通过'
$taskEntry.verification+='；R17原两个追加行为/三个客户快照契约转当前PG，原未关联量/贡献守恒/历史收货/软删流水门禁及客户快照断言保持；3离线28ms，最终2真PG130.9498ms通过。首次1通过1夹具触发器失败留证；SQL Server同样保护成立身份，夹具仅INSERT预设身份，共用初始化已变故两个原方法必要复验。最终50表52976原行摘要全同/新8留存，前轮6留存，两轮总14，库存序列95→96；源固定SHA/HEAD/消费者复核退出，最终编译0错误7旧警告3.41秒，最终包图/宿主仍待。'
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
    recorded_at=(Get-Date).ToString('o');stage='P09-R17累计源码保护，采购草稿原追加消费者转PG及一个旧源退出'
    fixed_snapshot_source_count=$taskProperties.Count;source_candidate_types_exited=$taskExited.Count
    expected_missing_count=$taskExpectedMissing.Count;missing=$taskMissing;changed_files=$taskChanged
    unexpected_production_changes=$taskUnexpectedProduction;unexpected_missing=$taskUnexpectedMissing;new_sources=$taskNew
    current_enumerated_sources=$taskEnumerated.Count;existing_fixed_snapshot_files=$taskExisting
    ignored_existing_unchanged_artifacts=$taskIgnored;database_run=$true;source_deletions_this_batch=1
}
$taskProtection|ConvertTo-Json -Depth 10|Set-Content -LiteralPath docs/postgresql-migration/任务结果/P09-R17-源码保护证据.json -Encoding utf8
if($taskProperties.Count -ne 2390 -or $taskExited.Count -ne 67 -or $taskMissing.Count -ne 73 -or $taskUnexpectedProduction.Count -ne 0 -or $taskUnexpectedMissing.Count -ne 0 -or $taskNew.Count -ne 0){throw '源码保护不符合本批边界'}
rg -l -w -g '*.cs' -g '*.csproj' -g '!弃用资料/**' -g '!**/obj/**' -g '!**/bin/**' -g '!.agents/**' -g '!.codex/**' -- PurchaseOrderDraftRepository .
if($LASTEXITCODE -ne 1){throw '退出后仍有旧采购草稿仓储引用或搜索失败'}
$taskBlocked='SwiftExport.Infrastructure/Repositories/DemandPlanning/ExecutionEligibilityRepository.cs'
if(-not(Test-Path -LiteralPath $taskBlocked)){$taskBlocked=@(rg --files SwiftExport.Infrastructure -g ExecutionEligibilityRepository.cs)[0]}
if((Get-FileHash -LiteralPath $taskBlocked -Algorithm SHA256).Hash -ne '0DDAE30D90FE9F268665AF4394C96BE24473030936AE39EDB4B6192B7DAF9378'){throw '待确认原文件发生变化'}
$taskManifest|ConvertTo-Json -Depth 15|Set-Content -LiteralPath $taskManifestPath -Encoding utf8

$taskRecordPath='docs/postgresql-migration/任务结果/P09-R17-采购草稿旧消费者-文件证据.json'
$taskRecord=Get-Content $taskRecordPath -Raw|ConvertFrom-Json
foreach($taskTest in $taskRecord.tests){
    if((Get-FileHash -LiteralPath $taskTest.path -Algorithm SHA256).Hash -ne $taskTest.after_sha256){throw '已通过测试源码又变化，不能复用通过事实'}
}
$taskRecord.status='本批完成：原采购追加2真PG/3客户快照离线通过，旧采购草稿源退出；整体P09未完成'
$taskRecord|Add-Member -NotePropertyName completed_at -NotePropertyValue (Get-Date).ToString('o')
$taskArtifactPaths=@(
    'TestResults/postgresql-migration/p09-r17-draft-customer-snapshot-offline.trx',
    'TestResults/postgresql-migration/p09-r17-draft-append-before-identity-seed-failed.trx',
    'TestResults/postgresql-migration/p09-r17-draft-data-before-identity-seed.json',
    'TestResults/postgresql-migration/p09-r17-draft-append-after-identity-seed-pg.trx',
    'TestResults/postgresql-migration/p09-r17-draft-data-protection.json'
)
$taskRecord|Add-Member -NotePropertyName execution -NotePropertyValue @{
    build_command='dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -v minimal'
    builds=@(@{exit=0;errors=0;existing_warnings=7;seconds=2.73;stage='首次受影响项目编译'},@{exit=0;errors=0;existing_warnings=7;seconds=2.73;stage='INSERT成立身份夹具修正编译'},@{exit=0;errors=0;existing_warnings=7;seconds=3.41;stage='旧源退出后必要编译，已通过测试和PG源SHA未变，不重跑数据库'})
    test_commands=@("& './docs/postgresql-migration/任务结果/P09-R17-采购客户快照-实际命令.ps1'","& './docs/postgresql-migration/任务结果/P09-R17-采购草稿追加-实际命令.ps1'","& './docs/postgresql-migration/任务结果/P09-R17-采购成立身份夹具修正-实际命令.ps1'")
    runs=@(@{total=3;passed=3;failed=0;skipped=0;duration='28ms';database=$false},@{total=2;passed=1;failed=1;skipped=0;duration='含50表保护约2秒';database=$true;cause='夹具更新ExecutionRequestId，现PG P3101拒绝；SQL Server生成基线同样禁止，旧夹具仅提取四表未装触发器'},@{total=2;passed=2;failed=0;skipped=0;methods_seconds=0.1309498;database=$true;scope='仅INSERT预设来源/执行身份，共用初始化已变，必要重跑两个原方法；所有业务断言保留'})
    database_runs=@(@{old_rows=52970;added_rows=6;protected_tables=50;old_rows_unchanged=$true;sequence_before=95;sequence_after=95},@{old_rows=52976;added_rows=8;protected_tables=50;old_rows_unchanged=$true;sequence_before=95;sequence_after=96})
    retained_added_rows=14;current_rows=52984
    artifacts=@($taskArtifactPaths|ForEach-Object{@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}})
    local_command_issues='两处只读路径猜测/rg字面通配路径导致读取失败，没有数据库或文件变更；随后先发现实际文件再读取，源码事实以实际路径为准。'
    other_database_clients_after=0;advisory_locks_after=0;test_processes_after=@(Get-Process -Name testhost,vstest.console -ErrorAction SilentlyContinue).Count
    full_test_run=$false;runtime_or_backup_write=$false;ddl=$false;commit_merge_push=$false
}
$taskRecord|ConvertTo-Json -Depth 17|Set-Content -LiteralPath $taskRecordPath -Encoding utf8
[pscustomobject]@{OriginalSources=$taskProperties.Count;ExitedTypes=$taskExited.Count;ExitedFiles=$taskMissing.Count;Remaining=76-$taskExited.Count;ChangedTests=@($taskChanged|Where-Object{$_ -like 'SwiftExport.Application.Tests/*'}).Count;UnexpectedProduction=$taskUnexpectedProduction.Count;UnexpectedMissing=$taskUnexpectedMissing.Count;NewSources=$taskNew.Count;IgnoredExisting=$taskIgnored.Count;TestProcesses=$taskRecord.execution.test_processes_after}|Format-List
