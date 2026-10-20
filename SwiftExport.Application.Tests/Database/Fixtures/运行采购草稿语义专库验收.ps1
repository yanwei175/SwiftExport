param([switch]$Initialize,[switch]$CompletePermissionSeed)

$ErrorActionPreference = 'Stop'
# 仅设置本轮测试门禁，不读取或输出密码；首次初始化与后续场景执行均不清理已有事实。
$env:SWIFTEXPORT_RUN_PURCHASE_DRAFT_PG = '1'
if ($Initialize) { $env:SWIFTEXPORT_INIT_PURCHASE_DRAFT_PG = '1' }
else { Remove-Item Env:SWIFTEXPORT_INIT_PURCHASE_DRAFT_PG -ErrorAction SilentlyContinue }
if ($CompletePermissionSeed) { $env:SWIFTEXPORT_COMPLETE_PURCHASE_DRAFT_PERMISSION_SEED = '1' }
else { Remove-Item Env:SWIFTEXPORT_COMPLETE_PURCHASE_DRAFT_PERMISSION_SEED -ErrorAction SilentlyContinue }
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Push-Location $taskRoot
try {
    dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --artifacts-path .artifacts/order-tracking --no-restore --filter 'FullyQualifiedName~PurchaseDraftLifecyclePostgreSqlTests' --logger 'trx;LogFileName=purchase-draft-lifecycle-pg.trx'
    if ($LASTEXITCODE -ne 0) { throw '采购草稿专库验收未通过；保留日志及数据库事实，不自动清理或重置。' }
}
finally {
    Pop-Location
    Remove-Item Env:SWIFTEXPORT_RUN_PURCHASE_DRAFT_PG -ErrorAction SilentlyContinue
    Remove-Item Env:SWIFTEXPORT_INIT_PURCHASE_DRAFT_PG -ErrorAction SilentlyContinue
    Remove-Item Env:SWIFTEXPORT_COMPLETE_PURCHASE_DRAFT_PERMISSION_SEED -ErrorAction SilentlyContinue
}
