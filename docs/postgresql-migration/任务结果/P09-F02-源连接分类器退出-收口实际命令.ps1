# P09-F02：无消费者源工厂类/分类器退出，保留原公共接口；固定源/一行审计XML/两行工厂XML保护，不连接库或重跑测试。
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskRecordPath='docs/postgresql-migration/任务结果/P09-F02-源连接分类器退出-文件证据.json'
$taskRecord=Get-Content $taskRecordPath -Raw|ConvertFrom-Json
$taskFactory='SwiftExport.Infrastructure/Data/DbConnectionFactory.cs'
$taskFactoryBefore=($taskRecord.files|Where-Object{$_.path -eq $taskFactory}).before_text
$taskMarker='/// <summary>按宿主提供的配置创建 SQL Server 连接，不改变现有数据库运行行为。</summary>'
$taskOffset=$taskFactoryBefore.IndexOf($taskMarker,[StringComparison]::Ordinal)
if($taskOffset -lt 0){throw '原工厂块起点不存在'}
$taskExpected=[regex]::Replace($taskFactoryBefore.Substring(0,$taskOffset),'(?m)^using Microsoft\.Data\.SqlClient;\r?\n','')
$taskExpected=$taskExpected.Replace('/// <summary>创建尚未打开的连接；会话负责打开和释放，提供者选择留在基础设施层。</summary>','/// <summary>创建尚未打开的PG连接；会话负责打开和释放，连接配置由基础设施接收。</summary>').Replace('/// <summary>默认使用目标PG异常分类器；迁移期间显式源工厂负责提供自己的分类器。</summary>','/// <summary>默认使用当前PG异常分类器，供会话保持既有失败分类边界。</summary>')
$taskActual=Get-Content -LiteralPath $taskFactory -Raw
if($taskActual.Replace("`r`n","`n").TrimEnd() -cne $taskExpected.Replace("`r`n","`n").TrimEnd()){throw '工厂变更超出退出旧类/using及两行XML职责说明'}
if(-not($taskActual.Contains('public interface IDbConnectionFactory') -and $taskActual.Contains('SwiftExport.Core.Interfaces.Data.IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance;') -and $taskActual.Contains('IDbConnection CreateConnection();'))){throw '原公共接口/PG默认分类表达式/连接签名未保留'}
$taskF01=Get-Content 'docs/postgresql-migration/任务结果/P09-F01-旧通用仓储退出-文件证据.json' -Raw|ConvertFrom-Json
$taskAudit=$taskF01.audit_comment
if((Get-FileHash -LiteralPath $taskAudit.path).Hash -ne $taskAudit.after_sha256){throw '已验审计一行XML后又变化'}
$taskAuditAfter=Get-Content -LiteralPath $taskAudit.path -Raw
if($taskAuditAfter.Replace($taskAudit.new_line,$taskAudit.old_line).Replace("`r`n","`n") -cne $taskAudit.before_text.Replace("`r`n","`n")){throw '审计变更超出F01一行XML'}
$taskManifestPath='docs/postgresql-migration/任务结果/P09-当前源退出文件清单.json'
$taskManifest=Get-Content $taskManifestPath -Raw|ConvertFrom-Json
foreach($taskName in @('SqlConnectionFactory','SqlServerExceptionClassifier')){
 $taskEntry=$taskManifest|Where-Object{$_.name -eq $taskName}
 if(@($taskEntry).Count -ne 1){throw '退出条目不唯一'}
 $taskEntry.status='验证通过：P09-F02无消费者旧源类型退出，公共接口保留及当前Infra构建通过；最终包/宿主待'
 $taskEntry.verification+='；F02固定原SHA/跟踪/当前HEAD无修改，全源只有两源定义互相引用，无外部消费者。只退出源工厂类/SqlClient using、源分类器，混合DbConnectionFactory文件保留原IDbConnectionFactory、默认PG分类表达式和CreateConnection签名；两行XML同步，结构重建完全相同。当前Infra0错误0警告0.84秒；不改PG工厂/分类器/会话/DI/配置/业务或包、不测试或连接数据库，最终S消费者/撤包/资产/宿主待。'
}
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
    recorded_at=(Get-Date).ToString('o');stage='P09-F02累计源码保护，旧连接工厂类及分类器退出、公共接口保留'
    fixed_snapshot_source_count=$taskProperties.Count;source_candidate_types_exited=$taskExited.Count
    expected_missing_count=$taskExpectedMissing.Count;missing=$taskMissing;changed_files=$taskChanged
    unexpected_production_changes=$taskUnexpectedProduction;unexpected_missing=$taskUnexpectedMissing;new_sources=$taskNew
    current_enumerated_sources=$taskEnumerated.Count;existing_fixed_snapshot_files=$taskExisting
    ignored_existing_unchanged_artifacts=$taskIgnored;database_run=$false;source_deletions_this_batch=1
}
$taskProtection|ConvertTo-Json -Depth 10|Set-Content -LiteralPath docs/postgresql-migration/任务结果/P09-F02-源码保护证据.json -Encoding utf8
if($taskProperties.Count -ne 2390 -or $taskExited.Count -ne 73 -or $taskMissing.Count -ne 81 -or $taskUnexpectedProduction.Count -ne 0 -or $taskUnexpectedMissing.Count -ne 0 -or $taskNew.Count -ne 0){throw '源码保护不符合本批边界'}

$taskBlocked=@(rg --files SwiftExport.Infrastructure -g ExecutionEligibilityRepository.cs)[0]
if((Get-FileHash -LiteralPath $taskBlocked).Hash -ne '0DDAE30D90FE9F268665AF4394C96BE24473030936AE39EDB4B6192B7DAF9378'){throw '待确认原源发生变化'}



rg -l -w -g '*.cs' -g '*.csproj' -g '!弃用资料/**' -g '!**/obj/**' -g '!**/bin/**' -g '!.agents/**' -g '!.codex/**' -- 'DapperRepository|SqlConnectionFactory|SqlServerExceptionClassifier' .
if($LASTEXITCODE -ne 1){throw '三个旧类型仍有引用或搜索失败'}
$taskFactoryBytes=[IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $taskFactory).Path)
if(-not($taskFactoryBytes.Length -ge 3 -and $taskFactoryBytes[0] -eq 239 -and $taskFactoryBytes[1] -eq 187 -and $taskFactoryBytes[2] -eq 191)){throw '原工厂UTF8 BOM未保留'}
$taskFactoryAfterHash=(Get-FileHash -LiteralPath $taskFactory).Hash
$taskProtection|Add-Member -NotePropertyName allowed_production_scope -NotePropertyValue @(
 @{path=$taskAudit.path;after_sha256=$taskAudit.after_sha256;scope='F01一行XML职责说明，原文本重建相同，行为不改'},
 @{path=$taskFactory;after_sha256=$taskFactoryAfterHash;scope='F02仅退出零消费者SqlConnectionFactory/using，原公共接口可执行内容保留及两行XML同步；逐文本重建相同，原BOM保留'}
)
$taskProtection|Add-Member -NotePropertyName retained_mixed_source -NotePropertyValue @{path=$taskFactory;source_type_exited='SqlConnectionFactory';interface_retained='IDbConnectionFactory';counts_as_missing_file=$false}
$taskProtection|ConvertTo-Json -Depth 12|Set-Content -LiteralPath 'docs/postgresql-migration/任务结果/P09-F02-源码保护证据.json' -Encoding utf8
$taskRecord|Add-Member -NotePropertyName factory_after_sha256 -NotePropertyValue $taskFactoryAfterHash
$taskRecord.status='本批验证通过：两无消费者源类型已退出，原公共接口/PG默认表达式/签名及原BOM保持；S消费者/最后包/资产/宿主仍待'
$taskRecord|Add-Member -NotePropertyName completed_at -NotePropertyValue (Get-Date).ToString('o')
$taskRecord|Add-Member -NotePropertyName execution -NotePropertyValue @{
 build_command='dotnet build SwiftExport.Infrastructure/SwiftExport.Infrastructure.csproj --no-restore -v minimal';build_exit=0;errors=0;warnings=0;seconds=0.84
 build_source_scope='编译后只移除本批造成的文件末空行，接口可执行内容/原BOM及换行规则未改；没有因此重复必要编译。'
 tests_run=$false;reason='无外部消费者两源类型退出，原共享接口可执行内容完全保留；必要Infra编译/全源零引用/固定文本重建即可，不重跑已绿或全量。'
 database_connection=$false;package_change=$false;business_or_pg_or_session_change=$false;public_interface_executable_content_preserved=$true;factory_utf8_bom_preserved=$true;commit_merge_push=$false
 old_type_references=0;original_sources=$taskProperties.Count;exited_types=$taskExited.Count;exited_files=$taskMissing.Count;remaining_candidate_types=76-$taskExited.Count
 changed_tests=@($taskChanged|Where-Object{$_ -like 'SwiftExport.Application.Tests/*'}).Count;unexpected_production_changes=$taskUnexpectedProduction.Count;unexpected_missing=$taskUnexpectedMissing.Count;new_sources=$taskNew.Count;test_processes_after=@(Get-Process -Name testhost,vstest.console -ErrorAction SilentlyContinue).Count
 source_protection='P09-F02-源码保护证据.json';local_command_issues='apply_patch首行匹配原UTF8 BOM失败，SHA确认无部分改动；随后限定原SHA/HEAD且保留原BOM的原生精确替换成功。diff发现仅本批文件末空行已清除；没有业务或数据库副作用，不是测试失败。'
 future_protection='混合Factory文件仍存在，不能纳入已删除文件。后续保护脚本须按当前73类型/81源文件及F01审计/F02Factory的精准afterSHA或文本重建允许范围核对；历史脚本保留当时范围，不直接重放。'
}
$taskRecord|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $taskRecordPath -Encoding utf8
$taskManifest|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $taskManifestPath -Encoding utf8
[pscustomobject]@{OriginalSources=$taskProperties.Count;ExitedTypes=$taskExited.Count;ExitedFiles=$taskMissing.Count;Remaining=76-$taskExited.Count;ChangedTests=$taskRecord.execution.changed_tests;RetainedPublicFactoryInterface=1;AllowedProductionScopes=2;UnexpectedProduction=$taskUnexpectedProduction.Count;UnexpectedMissing=$taskUnexpectedMissing.Count;NewSources=$taskNew.Count;TestProcesses=$taskRecord.execution.test_processes_after}|Format-List
