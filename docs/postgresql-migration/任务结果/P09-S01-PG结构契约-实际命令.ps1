# P09-S01：仅九个受影响的既有PG结构契约；关闭并恢复全部数据库门禁，不连接数据库。
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskCategories=@(rg -o --no-filename 'TestCategory\("[^"]+"' SwiftExport.Application.Tests|ForEach-Object{$_ -replace '^TestCategory\("','' -replace '"$',''}|Where-Object{$_ -match 'Integration$'}|Sort-Object -Unique)
$taskGates=@(rg -o --no-filename 'GetEnvironmentVariable\("SWIFTEXPORT_RUN_[^"]+"' SwiftExport.Application.Tests|ForEach-Object{$_ -replace '^GetEnvironmentVariable\("','' -replace '"$',''}|Sort-Object -Unique)
if($taskCategories.Count -ne 36 -or $taskGates.Count -ne 12){throw '门禁/分类变化，需重新核对范围'}
$taskMethods=@(
'EntityDrivenSchemaContractTests.SchemaGenerator_MustBeDeterministicAndContainNoForeignKeys',
'EntityDrivenSchemaContractTests.CommittedBaseline_MustMatchEntityMetadataAndKeepDataCopySeparate',
'ProductMasterSchemaContractTests.GeneratedSchema_ShouldCreateProductMasterAggregateConstraints',
'InventoryImmutableLedgerSchemaContractTests.GeneratedSchema_ShouldNotContainBusinessDataWrites',
'InventoryImmutableLedgerSchemaContractTests.Schema_ShouldProtectLedgerAndBalanceProjection',
'InventoryImmutableLedgerSchemaContractTests.GeneratedSchema_ShouldDeclareSequenceAndNoForeignKey',
'SalesSourceDemandContractTests.GeneratedSchemaUsesFullSourceIdentityWithoutHistoricalMigration',
'PackingListDraftApiContractTests.DraftPersistence_RequiresPositiveQuantityAndUniqueCreateCommand'
)
# 这里实际只有八个方法；不加入未受影响的实体/复制规则方法来凑数。
if($taskMethods.Count -ne 8){throw '定向方法范围错误'}
$taskFilter=(@($taskCategories|ForEach-Object{'TestCategory!='+$_})+@('FullyQualifiedName!~RealPg_','FullyQualifiedName!~SqlServer','FullyQualifiedName!~LocalDbTests','FullyQualifiedName!~SqlTests')) -join '&'
$taskFilter+='&('+ (($taskMethods|ForEach-Object{'FullyQualifiedName~'+$_}) -join '|') + ')'
$taskOriginal=@{};$taskExit=1
try{
 foreach($taskGate in $taskGates){$taskOriginal[$taskGate]=[Environment]::GetEnvironmentVariable($taskGate,'Process');[Environment]::SetEnvironmentVariable($taskGate,'0','Process')}
 dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter $taskFilter --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p09-s01-schema-contracts-offline.trx' -v minimal
 $taskExit=$LASTEXITCODE
}finally{foreach($taskGate in $taskGates){[Environment]::SetEnvironmentVariable($taskGate,$taskOriginal[$taskGate],'Process')}}
exit $taskExit
