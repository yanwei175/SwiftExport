# P09-R11旧拒绝条件复核：当前旧源已HEAD clean且无消费者，按既有SQLServer退出授权删除；原完整字节已留存，不提交/推送。
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskPath='SwiftExport.Infrastructure/Repositories/MasterData/ExecutionEligibilityRepository.cs'
$taskAbsolute=(Resolve-Path -LiteralPath $taskPath).Path
$taskRoot=(Resolve-Path -LiteralPath '.').Path.TrimEnd('\')+'\'
if(-not $taskAbsolute.StartsWith($taskRoot,[StringComparison]::OrdinalIgnoreCase)){throw '目标不在当前工作目录'}
$taskRecord=Get-Content docs/postgresql-migration/任务结果/P09-R11-执行资格源退出-文件证据.json -Raw|ConvertFrom-Json
$taskSaved=@($taskRecord.files|Where-Object{$_.path -eq $taskPath})[0]
$taskOriginalSha='0DDAE30D90FE9F268665AF4394C96BE24473030936AE39EDB4B6192B7DAF9378'
$taskSavedBytes=[Convert]::FromBase64String($taskSaved.prior_exact_bytes_base64)
if([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($taskSavedBytes)) -ne $taskOriginalSha -or (Get-FileHash -LiteralPath $taskAbsolute).Hash -ne $taskOriginalSha){throw '原字节保护或当前SHA不符'}
$taskStatus=@(git status --porcelain -- $taskPath)
$taskHead=(git rev-parse HEAD)
$taskHeadBlob=(git rev-parse ('HEAD:'+$taskPath))
if($LASTEXITCODE -ne 0){throw 'HEAD未跟踪旧源'}
$taskWorkingBlob=(git hash-object ('--path='+$taskPath) -- $taskPath)
if($LASTEXITCODE -ne 0 -or $taskStatus.Count -ne 0 -or $taskHeadBlob -ne $taskWorkingBlob){throw '旧源有未提交或未跟踪状态，禁止删除'}
$taskConsumers=@(rg -l --no-ignore -w ExecutionEligibilityRepository . -g '*.cs' -g '!弃用资料/**' -g '!**/bin/**' -g '!**/obj/**' -g '!.git/**' -g '!.codex/**' -g '!.agents/**')
if($taskConsumers.Count -ne 1 -or ($taskConsumers[0] -replace '^\.\\','' -replace '\\','/') -ne $taskPath){throw '旧类型仍有其他消费者'}
$taskEvidence=[ordered]@{recorded_at=(Get-Date).ToString('o');path=$taskPath;current_sha256=$taskOriginalSha;saved_original_bytes_sha256=$taskOriginalSha;head=$taskHead;head_blob=$taskHeadBlob;working_blob=$taskWorkingBlob;tracked_head_clean=$true;consumers=$taskConsumers;old_rejection_condition_changed=$true;previous_rejection_preserved=$true;current_source_exit_authorization='持续SQLServer源退出，当前无未提交成果需额外删除授权';removed=$false}
$taskEvidence|ConvertTo-Json -Depth 5|Set-Content -LiteralPath docs/postgresql-migration/任务结果/P09-R11-当前HEAD源退出-证据.json -Encoding utf8
Remove-Item -LiteralPath $taskAbsolute
if(Test-Path -LiteralPath $taskAbsolute){throw '旧源仍存在'}
$taskEvidence.removed=$true
$taskEvidence|ConvertTo-Json -Depth 5|Set-Content -LiteralPath docs/postgresql-migration/任务结果/P09-R11-当前HEAD源退出-证据.json -Encoding utf8
$taskManifestPath='docs/postgresql-migration/任务结果/P09-当前源退出文件清单.json'
$taskManifest=Get-Content $taskManifestPath -Raw|ConvertFrom-Json
$taskCandidate=@($taskManifest|Where-Object{$_.name -eq 'ExecutionEligibilityRepository'})
if($taskCandidate.Count -ne 1){throw '候选清单不唯一'}
$taskCandidate[0].status='验证通过：P09-R11当前HEAD无未提交差异/无消费者，旧源退出；必要编译仍待'
$taskCandidate[0]|Add-Member -NotePropertyName current_head_exit -NotePropertyValue @{evidence='P09-R11-当前HEAD源退出-证据.json';head=$taskHead;original_byte_sha256=$taskOriginalSha;reason='旧拒绝基于未提交四行；当前已HEAD clean且完整原字节留存，历史拒绝保留，按原迁移退出范围处理'}
$taskManifest|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $taskManifestPath -Encoding utf8
[pscustomobject]@{Removed=$true;HeadClean=$true;OriginalBytesPreserved=$true;Head=$taskHead}|ConvertTo-Json
