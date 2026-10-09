param([switch]$UpdateWeb)
# 隔离 HTTP 宿主仅导出真实契约及权限目录；不连接业务库、不运行写入测试。
$ErrorActionPreference = 'Stop'
$taskRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskArtifacts = Join-Path $PSScriptRoot '.artifacts'
New-Item -ItemType Directory -Path $taskArtifacts -Force | Out-Null
$previousSchema = $env:SWIFTEXPORT_WEB_OPENAPI_OUTPUT
$previousPermissions = $env:SWIFTEXPORT_WEB_PERMISSIONS_OUTPUT
Push-Location $taskRepo
try {
    $env:SWIFTEXPORT_WEB_OPENAPI_OUTPUT = Join-Path $taskArtifacts 'OpenAPI完整.json'
    $env:SWIFTEXPORT_WEB_PERMISSIONS_OUTPUT = Join-Path $taskArtifacts '权限目录.json'
    dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --artifacts-path (Join-Path $taskArtifacts '契约导出构建') --filter 'FullyQualifiedName~FirstWebHttpContractTests.OpenApi_MatchesFirstBatchJsonAndExports' -p:NuGetAudit=false --logger 'trx;LogFileName=契约导出.trx' --results-directory (Join-Path $taskArtifacts 'TestResults')
    if ($LASTEXITCODE -ne 0) { throw '真实契约导出失败；不更新 Web 快照。' }
    if ($UpdateWeb) {
        Set-Location (Join-Path $taskRepo 'SwiftExport.WebApp')
        node scripts/更新契约快照.mjs --openapi $env:SWIFTEXPORT_WEB_OPENAPI_OUTPUT --permissions $env:SWIFTEXPORT_WEB_PERMISSIONS_OUTPUT
        if ($LASTEXITCODE -ne 0) { throw '审核清单/权限投影核对失败。' }
        npm.cmd run generate
        if ($LASTEXITCODE -ne 0) { throw 'Orval 生成失败。' }
        npm.cmd run generate:check
        if ($LASTEXITCODE -ne 0) { throw '生成差异核对失败。' }
        npm.cmd run typecheck
        if ($LASTEXITCODE -ne 0) { throw '生成接入类型检查失败。' }
    }
}
finally {
    $env:SWIFTEXPORT_WEB_OPENAPI_OUTPUT = $previousSchema
    $env:SWIFTEXPORT_WEB_PERMISSIONS_OUTPUT = $previousPermissions
    Pop-Location
}
