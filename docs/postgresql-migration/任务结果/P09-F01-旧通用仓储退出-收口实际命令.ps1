# P09-F01：旧通用仓储退出及审计一行XML同步，固定源保护/当前Infra构建；不连接数据库或重跑测试。
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskRecordPath='docs/postgresql-migration/任务结果/P09-F01-旧通用仓储退出-文件证据.json'
$taskRecord=Get-Content $taskRecordPath -Raw|ConvertFrom-Json
$taskAudit=$taskRecord.audit_comment
$taskAfter=Get-Content -LiteralPath $taskAudit.path -Raw
if(($taskAfter.Replace($taskAudit.new_line,$taskAudit.old_line).Replace("`r`n","`n")) -cne ($taskAudit.before_text.Replace("`r`n","`n"))){throw '审计变更超出一行XML职责说明'}
$taskFacts=Get-Content docs/postgresql-migration/任务结果/P09-当前源退出引用事实.json -Raw|ConvertFrom-Json
if($taskAudit.before_sha256 -ne $taskFacts.source_hashes.PSObject.Properties[$taskAudit.path].Value){throw '审计修改前不是固定原快照，不能按一行说明验收'}
$taskManifestPath='docs/postgresql-migration/任务结果/P09-当前源退出文件清单.json'
$taskManifest=Get-Content $taskManifestPath -Raw|ConvertFrom-Json
$taskEntry=$taskManifest|Where-Object{$_.name -eq 'DapperRepository'}
if(@($taskEntry).Count -ne 1 -or (Test-Path -LiteralPath $taskRecord.source.path)){throw '旧通用源尚存在或清单不唯一'}
$taskEntry.status='验证通过：P09-F01无消费者旧通用仓储已退出，当前Infra必要构建通过；最终包图宿主待'
$taskEntry.verification+='；F01固定SHA/跟踪/HEAD无修改及全源引用核验，仅定义与一行XML说明，无运行/测试消费者；旧源退出，审计说明指现有PG通用仓储，重建原文只替换一行XML完全一致，行为不改。当前Infra构建0错误0警告0.85秒通过；不重跑已绿通用/真实自动过账测试，不连接数据库、不撤包，其他S/F源消费者及最终宿主仍待。'
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
        if($taskPath -notlike 'SwiftExport.Application.Tests/*' -and $taskPath -notin @('_tools/DbSeed/DbSeed.csproj','_tools/AdminPasswordRecovery/AdminPasswordRecovery.csproj','SwiftExport.Infrastructure/Services/Auth/AuditStampService.cs')){$taskUnexpectedProduction+=$taskPath}
    }
}
$taskEnumerated=@(rg --files -g '*.cs' -g '*.csproj' -g '!弃用资料/**' -g '!**/obj/**' -g '!**/bin/**' -g '!.agents/**' -g '!.codex/**'|ForEach-Object{$_ -replace '\\','/'})
if($LASTEXITCODE -ne 0){throw '源码枚举失败'}
$taskFixedPaths=@($taskProperties|ForEach-Object{$_.Name})
$taskNew=@($taskEnumerated|Where-Object{$_ -notin $taskFixedPaths})
$taskIgnored=@($taskFixedPaths|Where-Object{$_ -notin $taskEnumerated -and $_ -notin $taskMissing})
$taskProtection=[ordered]@{
    recorded_at=(Get-Date).ToString('o');stage='P09-F01累计源码保护，无消费者旧通用仓储退出及一行XML职责同步'
    fixed_snapshot_source_count=$taskProperties.Count;source_candidate_types_exited=$taskExited.Count
    expected_missing_count=$taskExpectedMissing.Count;missing=$taskMissing;changed_files=$taskChanged
    unexpected_production_changes=$taskUnexpectedProduction;unexpected_missing=$taskUnexpectedMissing;new_sources=$taskNew
    current_enumerated_sources=$taskEnumerated.Count;existing_fixed_snapshot_files=$taskExisting
    ignored_existing_unchanged_artifacts=$taskIgnored;database_run=$false;source_deletions_this_batch=1
}
$taskProtection|ConvertTo-Json -Depth 10|Set-Content -LiteralPath docs/postgresql-migration/任务结果/P09-F01-源码保护证据.json -Encoding utf8
if($taskProperties.Count -ne 2390 -or $taskExited.Count -ne 71 -or $taskMissing.Count -ne 80 -or $taskUnexpectedProduction.Count -ne 0 -or $taskUnexpectedMissing.Count -ne 0 -or $taskNew.Count -ne 0){throw '源码保护不符合本批边界'}

$taskBlocked=@(rg --files SwiftExport.Infrastructure -g ExecutionEligibilityRepository.cs)[0]
if((Get-FileHash -LiteralPath $taskBlocked).Hash -ne '0DDAE30D90FE9F268665AF4394C96BE24473030936AE39EDB4B6192B7DAF9378'){throw '待确认原源发生变化'}


rg -l -w -g '*.cs' -g '*.csproj' -g '!弃用资料/**' -g '!**/obj/**' -g '!**/bin/**' -g '!.agents/**' -g '!.codex/**' -- 'DapperRepository' .
if($LASTEXITCODE -ne 1){throw '旧通用仓储仍有引用或搜索失败'}
$taskProtection|Add-Member -NotePropertyName allowed_production_comment_change -NotePropertyValue @{
 path=$taskAudit.path;before_sha256=$taskAudit.before_sha256;after_sha256=(Get-FileHash -LiteralPath $taskAudit.path).Hash
 reconstructed_original_except_one_xml_line=$true;code_or_audit_behavior_change=$false
}
$taskProtection|ConvertTo-Json -Depth 12|Set-Content -LiteralPath 'docs/postgresql-migration/任务结果/P09-F01-源码保护证据.json' -Encoding utf8
$taskAudit|Add-Member -NotePropertyName after_sha256 -NotePropertyValue (Get-FileHash -LiteralPath $taskAudit.path).Hash
$taskAudit|Add-Member -NotePropertyName only_one_xml_line_changed -NotePropertyValue $true
$taskRecord.status='本批验证通过：旧无消费者DapperRepository源退出及一行审计XML说明同步；F整体撤包/资产/宿主未完成'
$taskRecord|Add-Member -NotePropertyName completed_at -NotePropertyValue (Get-Date).ToString('o')
$taskRecord|Add-Member -NotePropertyName execution -NotePropertyValue @{
 build_command='dotnet build SwiftExport.Infrastructure/SwiftExport.Infrastructure.csproj --no-restore -v minimal';build_exit=0;errors=0;warnings=0;build_seconds=0.85
 tests_run=$false;reason='只退无消费者固定源和一行XML说明，PG/审计可执行代码未改；必要受影响项目编译及零引用/完整源码保护足够，本批不重复通用/实库绿色或全量。此前历史通过仍作为带SHA历史证据，不标本批已重跑。'
 database_connection=$false;ddl=$false;package_change=$false;business_or_audit_behavior_change=$false;new_pg_repository_or_interface=$false;commit_merge_push=$false
 exited_types=$taskExited.Count;exited_files=$taskMissing.Count;remaining_types=76-$taskExited.Count;unexpected_production_changes=$taskUnexpectedProduction.Count;unexpected_missing=$taskUnexpectedMissing.Count;new_sources=$taskNew.Count;changed_tests=@($taskChanged|Where-Object{$_ -like 'SwiftExport.Application.Tests/*'}).Count
 source_protection='P09-F01-源码保护证据.json';old_type_references=0;test_processes_after=@(Get-Process -Name testhost,vstest.console -ErrorAction SilentlyContinue).Count
 future_protection='后续源码核对须明确允许本批一行XML变更并校验该afterSHA或原文重建，不能把任意AuditStampService行为变更列入允许范围；历史脚本按当时边界保留。'
}
$taskRecord|ConvertTo-Json -Depth 15|Set-Content -LiteralPath $taskRecordPath -Encoding utf8
$taskManifest|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $taskManifestPath -Encoding utf8
[pscustomobject]@{OriginalSources=$taskProperties.Count;ExitedTypes=$taskExited.Count;ExitedFiles=$taskMissing.Count;Remaining=76-$taskExited.Count;ChangedTests=$taskRecord.execution.changed_tests;AllowedProductionXmlLines=1;UnexpectedProduction=$taskUnexpectedProduction.Count;UnexpectedMissing=$taskUnexpectedMissing.Count;NewSources=$taskNew.Count;TestProcesses=$taskRecord.execution.test_processes_after}|Format-List
