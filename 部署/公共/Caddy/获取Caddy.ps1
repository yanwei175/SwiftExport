#Requires -Version 5.1
<#! 下载固定官方 Windows x64 版本并核对官方 SHA512；可在开发电脑执行后连同工具离线复制到服务器。 !#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$version = '2.11.7'
$expected = 'c308154504e53755958ffc62d8a2657ab17eefd6dd25c45dbb2c3d93d29519971f77ca652f7d0f2bcb7f980b9ba3822478865e29969d0272d7613550a43152b1'
$destination = Join-Path $PSScriptRoot 'bin'
[void][IO.Directory]::CreateDirectory($destination)
$archive = Join-Path $destination ('caddy_' + $version + '_windows_amd64.zip')
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/caddyserver/caddy/releases/download/v$version/caddy_${version}_windows_amd64.zip" -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $expected) { throw 'Caddy 官方发布包校验失败，停止解压。' }
Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
Write-Output ('Caddy ' + $version + ' 已准备：' + (Join-Path $destination 'caddy.exe'))
