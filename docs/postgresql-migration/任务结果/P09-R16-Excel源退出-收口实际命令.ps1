# P09-R16：登记已经验证的单个旧源退出，核对原2390源码/项目快照；不重复测试、不连接数据库。
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskManifestPath='docs/postgresql-migration/任务结果/P09-当前源退出文件清单.json'
$taskManifest=Get-Content $taskManifestPath -Raw|ConvertFrom-Json
$taskEntry=$taskManifest|Where-Object name -eq 'ExcelReadProfileRepository'
if(Test-Path -LiteralPath $taskEntry.definitions[0].path){throw '旧Excel仓储仍存在'}
$taskEntry.status='验证通过：P09-R16源文件已退出，原生命周期1真PG/必要构建通过'
$taskEntry.verification+='；R16原LocalDB生命周期转当前PG，原版本/分步保存/软删/同组唯一/跨组同名/事务归属/写后失败回滚/真实SQL等待取消断言保持；failure实库1通过423.5846ms，50表原52965行全部摘要同，新增3头2字段留存，库存序列95不变。旧SHA/HEAD/跟踪/消费者复核后退出，最终必要编译0错误7旧警告4.11秒；最终包图/宿主仍待。'
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
    recorded_at=(Get-Date).ToString('o');stage='P09-R16累计源码保护，Excel原生命周期转PG及一个旧源退出'
    fixed_snapshot_source_count=$taskProperties.Count;source_candidate_types_exited=$taskExited.Count
    expected_missing_count=$taskExpectedMissing.Count;missing=$taskMissing;changed_files=$taskChanged
    unexpected_production_changes=$taskUnexpectedProduction;unexpected_missing=$taskUnexpectedMissing;new_sources=$taskNew
    current_enumerated_sources=$taskEnumerated.Count;existing_fixed_snapshot_files=$taskExisting
    ignored_existing_unchanged_artifacts=$taskIgnored;database_run=$true;source_deletions_this_batch=1
}
$taskProtection|ConvertTo-Json -Depth 10|Set-Content -LiteralPath docs/postgresql-migration/任务结果/P09-R16-源码保护证据.json -Encoding utf8
if($taskProperties.Count -ne 2390 -or $taskExited.Count -ne 66 -or $taskMissing.Count -ne 72 -or $taskUnexpectedProduction.Count -ne 0 -or $taskUnexpectedMissing.Count -ne 0 -or $taskNew.Count -ne 0){throw '源码保护不符合本批边界'}
rg -l -w -g '*.cs' -g '*.csproj' -g '!弃用资料/**' -g '!**/obj/**' -g '!**/bin/**' -g '!.agents/**' -g '!.codex/**' -- ExcelReadProfileRepository .
if($LASTEXITCODE -ne 1){throw '退出后仍有旧Excel仓储引用或搜索失败'}
$taskBlocked='SwiftExport.Infrastructure/Repositories/DemandPlanning/ExecutionEligibilityRepository.cs'
if(-not(Test-Path -LiteralPath $taskBlocked)){$taskBlocked=@(rg --files SwiftExport.Infrastructure -g ExecutionEligibilityRepository.cs)[0]}
if((Get-FileHash -LiteralPath $taskBlocked -Algorithm SHA256).Hash -ne '0DDAE30D90FE9F268665AF4394C96BE24473030936AE39EDB4B6192B7DAF9378'){throw '待确认原文件发生变化'}
$taskManifest|ConvertTo-Json -Depth 15|Set-Content -LiteralPath $taskManifestPath -Encoding utf8
$taskRecordPath='docs/postgresql-migration/任务结果/P09-R16-Excel旧消费者-文件证据.json'
$taskRecord=Get-Content $taskRecordPath -Raw|ConvertFrom-Json
if((Get-FileHash -LiteralPath $taskRecord.test.path -Algorithm SHA256).Hash -ne $taskRecord.test.after_sha256){throw '已通过测试文件又变化，不能复用通过事实'}
$taskRecord.status='本批完成：原生命周期1真PG通过，旧Excel仓储退出；整体P09未完成'
$taskRecord|Add-Member -NotePropertyName completed_at -NotePropertyValue (Get-Date).ToString('o')
$taskRecord|Add-Member -NotePropertyName execution -NotePropertyValue @{
    build_command='dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -v minimal'
    builds=@(@{exit=1;errors=2;warnings=7;seconds=4.95;reason='本批测试缺少PostgreSqlParameters命名空间，未运行测试或数据库写入'},@{exit=0;errors=0;warnings=8;seconds=5.38;reason='新增可空性警告，随后本批修正'},@{exit=0;errors=0;warnings=7;seconds=3.54;reason='实库前最终编译'},@{exit=0;errors=0;warnings=7;seconds=4.11;reason='退出旧源后必要编译，测试与PG源未变，不重跑实库'})
    test_command="& './docs/postgresql-migration/任务结果/P09-R16-Excel原生命周期-实际命令.ps1'"
    tests=@{total=1;passed=1;failed=0;skipped=0;method_seconds=0.4235846;whole_test_host_seconds='约2秒，含50表保护'}
    artifacts=@('TestResults/postgresql-migration/p09-r16-excel-lifecycle-pg.trx','TestResults/postgresql-migration/p09-r16-excel-data-protection.json')|ForEach-Object{@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}}
    local_command_issues='首次本地缩进整理PowerShell -replace运算符组合错误及源码Set-Content写权限拒绝，源未改/无数据库动作；已通过apply_patch与授权PS7原生写入处理，不改ACL。首次摘要展示Where-Object快捷写法把After当文字，误显示ChangedOld=50；用真实属性比较复核为0，原测试及证据始终50表相同。'
    other_database_clients_after=0;advisory_locks_after=0;test_processes_after=@(Get-Process -Name testhost,vstest.console -ErrorAction SilentlyContinue).Count
    full_test_run=$false;repeated_green_database_run=$false;runtime_or_backup_write=$false;ddl=$false;commit_merge_push=$false
}
$taskRecord|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $taskRecordPath -Encoding utf8
[pscustomobject]@{OriginalSources=$taskProperties.Count;ExitedTypes=$taskExited.Count;ExitedFiles=$taskMissing.Count;Remaining=76-$taskExited.Count;ChangedTests=@($taskChanged|Where-Object{$_ -like 'SwiftExport.Application.Tests/*'}).Count;UnexpectedProduction=$taskUnexpectedProduction.Count;UnexpectedMissing=$taskUnexpectedMissing.Count;NewSources=$taskNew.Count;IgnoredExisting=$taskIgnored.Count;TestProcesses=$taskRecord.execution.test_processes_after}|Format-List
