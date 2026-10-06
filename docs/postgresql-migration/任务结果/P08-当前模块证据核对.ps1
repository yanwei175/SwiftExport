# 主控当前模块证据核对：只读源码及已有TRX；不连接数据库、不运行测试、不读取配置秘密。
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskPrior=Get-Content docs/postgresql-migration/任务结果/P09-当前源退出引用事实.json -Raw|ConvertFrom-Json -AsHashtable
$taskChanges=@()
foreach($taskPath in $taskPrior.source_hashes.Keys|Sort-Object){
    if(-not(Test-Path -LiteralPath $taskPath)){$taskChanges+=[ordered]@{path=$taskPath;state='missing';prior_sha256=$taskPrior.source_hashes[$taskPath]};continue}
    $taskHash=(Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash
    if($taskHash -ne $taskPrior.source_hashes[$taskPath]){$taskChanges+=[ordered]@{path=$taskPath;state='changed';prior_sha256=$taskPrior.source_hashes[$taskPath];current_sha256=$taskHash}}
}
$taskCurrent=@(rg --files --no-ignore -g '*.cs' -g '*.csproj' -g '!弃用资料/**' -g '!.git/**' -g '!.codex/**' -g '!.agents/**' -g '!**/obj/**' -g '!**/bin/**'|ForEach-Object{$_.Replace('\','/')})
$taskAdded=@($taskCurrent|Where-Object{-not $taskPrior.source_hashes.ContainsKey($_)}|Sort-Object)
$taskTrxFacts=@()
foreach($taskFile in Get-ChildItem TestResults/postgresql-migration -Filter '*.trx' -File|Sort-Object Name){
    [xml]$taskXml=Get-Content -LiteralPath $taskFile.FullName -Raw
    $taskDefinitions=@{}
    foreach($taskDefinition in $taskXml.TestRun.TestDefinitions.UnitTest){$taskDefinitions[[string]$taskDefinition.id]=[string]$taskDefinition.TestMethod.className}
    $taskResults=@()
    foreach($taskResult in $taskXml.TestRun.Results.UnitTestResult){
        $taskResults+=[ordered]@{name=[string]$taskResult.testName;class_name=$taskDefinitions[[string]$taskResult.testId];outcome=[string]$taskResult.outcome;duration=[string]$taskResult.duration}
    }
    $taskTrxFacts+=[ordered]@{path='TestResults/postgresql-migration/'+$taskFile.Name;sha256=(Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash;created=[string]$taskXml.TestRun.Times.creation;counters=$taskXml.TestRun.ResultSummary.Counters.OuterXml;results=$taskResults}
}
$taskClassMap=[ordered]@{
    M01=@('PostgreSqlProductCatalogMigrationIntegrationTests','PostgreSqlProductSynchronizationIntegrationTests','PostgreSqlProductAttachmentsIntegrationTests','PostgreSqlProductSalesLookupIntegrationTests')
    M02=@('PostgreSqlProductMasterMigrationIntegrationTests','PostgreSqlProductNeutralMigrationIntegrationTests','PostgreSqlProductNeutralManagerMigrationIntegrationTests','PostgreSqlProductCategoryMigrationIntegrationTests','PostgreSqlProductColorMigrationIntegrationTests','PostgreSqlProductUniquenessIntegrationTests')
    M04=@('PostgreSqlCustomerMigrationIntegrationTests','PostgreSqlCustomerProductMigrationIntegrationTests','PostgreSqlSupplierMigrationIntegrationTests','PostgreSqlSupplierProductMigrationIntegrationTests')
    M05=@('PostgreSqlCurrencyMigrationIntegrationTests')
    M06=@('PostgreSqlAuthMigrationIntegrationTests')
    M07=@('PostgreSqlWarehouseQueryMigrationIntegrationTests','PostgreSqlWarehouseCandidateMigrationIntegrationTests')
}
# 共享宿主测试按本任务精确方法归属，不能把整类历史结果计入字典/配置模块。
$taskMethodMap=@{M05=@('RealPg_RuntimeDictionaryBatchParentAndAtomicRollback','RealPg_RuntimeExcelProfileSnapshotAndVersionCompetition')}
# 核对类名必须先与真实声明比对，缺匹配不可声称没有测试。
$taskModuleRows=@()
foreach($taskModule in $taskClassMap.Keys){
    $taskEvidence=@()
    foreach($taskTrx in $taskTrxFacts){
        # 按完整命名空间和类名匹配，避免主档/汇率或普通/原生类混淆。
        $taskFullNames=@($taskClassMap[$taskModule]|ForEach-Object{'SwiftExport.Application.Tests.Database.'+$_})
        $taskRows=@($taskTrx.results|Where-Object{
            ($taskFullNames -ccontains $_.class_name) -or
            ($_.class_name -ceq 'SwiftExport.Application.Tests.Database.PostgreSqlRuntimeWiringTests' -and $taskMethodMap.ContainsKey($taskModule) -and $taskMethodMap[$taskModule] -ccontains $_.name)
        })
        if($taskRows.Count -gt 0){$taskEvidence+=[ordered]@{trx=$taskTrx.path;sha256=$taskTrx.sha256;created=$taskTrx.created;results=$taskRows}}
    }
    $taskModuleRows+=[ordered]@{module=$taskModule;candidate_classes=$taskClassMap[$taskModule];candidate_runtime_methods=$taskMethodMap[$taskModule];existing_trx_evidence=$taskEvidence;limitation='仅已有TRX元数据定位；原生身份及目标需按源码/原执行登记核验，不将历史通过提升为本次重跑或完整模块通过'}
}
$taskManifest=@($taskTrxFacts|ForEach-Object{[ordered]@{path=$_.path;sha256=$_.sha256;created=$_.created;counters=$_.counters}})
$taskEvidence=[ordered]@{recorded_at=(Get-Date).ToString('o');prior_source_snapshot=$taskPrior.recorded_at;prior_source_count=$taskPrior.source_count;current_source_count=$taskCurrent.Count;changes=$taskChanges;added=$taskAdded;trx_count=$taskTrxFacts.Count;modules=$taskModuleRows;trx_manifest=$taskManifest;scope='源码/项目SHA与既有TRX元数据；只存文件清单及命中模块结果，不复制全套测试正文；不执行测试或数据库命令'}
$taskEvidence|ConvertTo-Json -Depth 13|Set-Content docs/postgresql-migration/任务结果/P08-当前模块证据核对.json -Encoding utf8
[ordered]@{prior_source_count=$taskPrior.source_count;current_source_count=$taskCurrent.Count;changes=$taskChanges;added=$taskAdded;trx_count=$taskTrxFacts.Count;modules=@($taskModuleRows|ForEach-Object{[ordered]@{module=$_.module;trx_count=$_.existing_trx_evidence.Count;results=@($_.existing_trx_evidence|ForEach-Object{$_.results}).Count}})}|ConvertTo-Json -Depth 7
