# P09-S04：旧生成器退出；核对2390原源和此前F01/F02精准范围、本批数据与新程序集，复用通过事实，不连接库或重跑测试。
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
    recorded_at=(Get-Date).ToString('o');stage='P09-S04累计源码保护，旧生成器/工具双库分支退出'
    fixed_snapshot_source_count=$taskProperties.Count;source_candidate_types_exited=$taskExited.Count
    expected_missing_count=$taskExpectedMissing.Count;missing=$taskMissing;changed_files=$taskChanged
    unexpected_production_changes=$taskUnexpectedProduction;unexpected_missing=$taskUnexpectedMissing;new_sources=$taskNew
    current_enumerated_sources=$taskEnumerated.Count;existing_fixed_snapshot_files=$taskExisting
    ignored_existing_unchanged_artifacts=$taskIgnored;database_run=$false;source_deletions_this_batch=1
}
$taskProtection|ConvertTo-Json -Depth 10|Set-Content -LiteralPath docs/postgresql-migration/任务结果/P09-S04-源码保护证据.json -Encoding utf8
if($taskProperties.Count -ne 2390 -or $taskExited.Count -ne 74 -or $taskMissing.Count -ne 82 -or $taskUnexpectedProduction.Count -ne 0 -or $taskUnexpectedMissing.Count -ne 0 -or $taskNew.Count -ne 0){throw '源码保护不符合本批边界'}

$taskBlocked=@(rg --files SwiftExport.Infrastructure -g ExecutionEligibilityRepository.cs)[0]
if((Get-FileHash -LiteralPath $taskBlocked).Hash -ne '0DDAE30D90FE9F268665AF4394C96BE24473030936AE39EDB4B6192B7DAF9378'){throw '待确认原源发生变化'}

# 只允许本批工具入口的已审查SHA，不能以路径白名单放宽其他生产变更。
$taskRecordPath='docs/postgresql-migration/任务结果/P09-S04-旧结构生成分支-文件证据.json'
$taskRecord=Get-Content $taskRecordPath -Raw|ConvertFrom-Json
$taskTool=@($taskRecord.files|Where-Object{$_.path -eq '_tools/SchemaGenerator/Program.cs'})
if($taskTool.Count -ne 1 -or (Get-FileHash -LiteralPath $taskTool[0].path).Hash -ne $taskTool[0].after_sha256){throw '工具精准范围变化'}
if(@($taskChanged|Where-Object{$_ -like 'SwiftExport.Application.Tests/*'}).Count -ne 52){throw '已验测试范围变化'}
foreach($taskBaseline in $taskRecord.baselines){
    if((Get-FileHash -LiteralPath $taskBaseline.path).Hash -ne $taskBaseline.before_sha256 -or $taskBaseline.after_sha256 -ne $taskBaseline.before_sha256){throw '原基线字节变化'}
}
$taskReferences=@(rg -l -w --no-ignore 'SqlServerSchemaGenerator' SwiftExport.Infrastructure SwiftExport.Application.Tests SwiftExport.WebApi SwiftExport.Application SwiftExport.Core SwiftExport.Contracts _tools -g '*.cs' -g '!**/obj/**' -g '!**/bin/**')
if($taskReferences.Count -ne 0 -or (Test-Path -LiteralPath $taskRecord.source_retirement.path)){throw '旧生成器仍存在或有消费者'}
$taskFixtureConsumers=@(rg -l -w 'IndependentProcurementSqlFixture' SwiftExport.Application.Tests --glob '*.cs')
if($taskFixtureConsumers.Count -ne 12){throw '本批未授权迁移公共LocalDB夹具'}
$taskAssembly='_tools/SchemaGenerator/bin/Debug/net10.0/SchemaGenerator.dll'
if((Get-Item -LiteralPath $taskAssembly).LastWriteTime -ge [datetime]$taskRecord.execution.started_at){throw '工具当前程序集未在运行前完成'}
$taskProtection|Add-Member -NotePropertyName allowed_production_scope -NotePropertyValue @(
 @{path=$taskAudit.path;after_sha256=$taskAudit.after_sha256;scope='此前F01一行XML，当前未再变'},
 @{path=$taskFactory;after_sha256=$taskF02.factory_after_sha256;scope='此前F02旧类退出/接口保持及两行XML，当前未再变'},
 @{path=$taskTool[0].path;after_sha256=$taskTool[0].after_sha256;scope='本批结构工具移除源分支与provider参数，PG生成算法/旧SQL文件保持'}
)
$taskProtection|Add-Member -NotePropertyName retained_mixed_source -NotePropertyValue @{path=$taskFactory;interface_retained='IDbConnectionFactory';counts_as_missing_file=$false}
$taskProtection|ConvertTo-Json -Depth 12|Set-Content -LiteralPath 'docs/postgresql-migration/任务结果/P09-S04-源码保护证据.json' -Encoding utf8
$taskRecord.status='验证通过：旧生成器/工具源分支退出，当前PG生成verify和源码/基线保护通过；其他S及最终包仍待'
$taskRecord|Add-Member -NotePropertyName completed_at -NotePropertyValue (Get-Date).ToString('o')
$taskRecord.execution|Add-Member -NotePropertyName closure -NotePropertyValue @{
    command="& './docs/postgresql-migration/任务结果/P09-S04-旧结构生成分支-收口实际命令.ps1'"
    original_sources=$taskProperties.Count;exited_types=$taskExited.Count;exited_source_files=$taskMissing.Count
    changed_existing_tests=52;unexpected_production_changes=$taskUnexpectedProduction.Count
    unexpected_missing=$taskUnexpectedMissing.Count;new_sources=$taskNew.Count;remaining_candidate_types=2
    old_generator_source_references=$taskReferences.Count;localdb_fixture_consumers_including_definition=$taskFixtureConsumers.Count
    source_proof='P09-S04-源码保护证据.json';compiled_assembly=@{path=$taskAssembly;sha256=(Get-FileHash -LiteralPath $taskAssembly).Hash;last_write=(Get-Item -LiteralPath $taskAssembly).LastWriteTime.ToString('o');fresh_before_run=$true}
    test_processes=@(Get-Process -Name testhost,vstest.console -ErrorAction SilentlyContinue).Count
}
$taskRecord|Add-Member -NotePropertyName remaining_consumers -NotePropertyValue @{
    sql_baseline=@(rg -l 'SwiftExport\.Schema\.sql' SwiftExport.Application.Tests _tools --glob '*.cs')
    localdb_fixture=$taskFixtureConsumers
}
$taskRecord|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $taskRecordPath -Encoding utf8
[pscustomobject]@{OriginalSources=$taskProperties.Count;ExitedTypes=$taskExited.Count;ExitedFiles=$taskMissing.Count;UnexpectedProduction=$taskUnexpectedProduction.Count;UnexpectedMissing=$taskUnexpectedMissing.Count;NewSources=$taskNew.Count;OldGeneratorReferences=$taskReferences.Count;LocalDbConsumers=$taskFixtureConsumers.Count;DatabaseRun=$false}|Format-List





