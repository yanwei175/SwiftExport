param([switch]$SkipNpmInstall)
# 在临时 HTTP 宿主导出 OpenAPI，再生成并检查 TypeScript；不启动正式服务器或连接数据库。
$ErrorActionPreference = 'Stop'
$repoPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifactPath = Join-Path $PSScriptRoot '.artifacts'
New-Item -ItemType Directory -Path $artifactPath -Force | Out-Null
$previousExport = $env:SWIFTEXPORT_WEB_OPENAPI_OUTPUT
$previousScratch = $env:NUGET_SCRATCH
Push-Location $repoPath
try {
    $env:SWIFTEXPORT_WEB_OPENAPI_OUTPUT = Join-Path $artifactPath 'OpenAPI完整.json'
    $env:NUGET_SCRATCH = Join-Path $artifactPath 'nuget-scratch'
    $filter = 'FullyQualifiedName~FirstWebHttpContractTests|FullyQualifiedName~ProductBomSaveBoundaryTests|FullyQualifiedName~UpdateIdentityTests|FullyQualifiedName~CustomerUpdateConcurrencyTests|FullyQualifiedName~EndpointPermissionFilterTests|FullyQualifiedName~EndpointPermissionHttpTests|FullyQualifiedName~HttpErrorContractTests|FullyQualifiedName~JwtAndControllerContractTests|FullyQualifiedName~AuthServiceTests|FullyQualifiedName~PermissionCatalogConsistencyTests|FullyQualifiedName~ProductDefaultWarehouseCandidateTests'
    dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --artifacts-path $artifactPath --filter $filter -p:NuGetAudit=false --logger 'trx;LogFileName=Web契约验收.trx' --results-directory (Join-Path $artifactPath 'TestResults')
    if ($LASTEXITCODE -ne 0) { throw '定向 HTTP / 业务边界测试失败。' }
    dotnet build SwiftExport-WebAPI.slnx --artifacts-path $artifactPath -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw '后端构建失败。' }
    Set-Location $PSScriptRoot
    if (-not $SkipNpmInstall) {
        npm.cmd ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw '固定 npm 依赖安装失败。' }
    }
    npm.cmd run verify
    if ($LASTEXITCODE -ne 0) { throw 'OpenAPI / Orval / TypeScript 验证失败。' }
}
finally {
    $env:SWIFTEXPORT_WEB_OPENAPI_OUTPUT = $previousExport
    $env:NUGET_SCRATCH = $previousScratch
    Pop-Location
}
