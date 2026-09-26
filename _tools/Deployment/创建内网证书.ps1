#Requires -Version 5.1
#Requires -RunAsAdministrator
<#!
在受控的管理员电脑上创建一次内部 CA，或使用已有 CA 签发服务器证书。
客户端只分发 Root.cer；Server.pfx 与 CA 私钥不能放进客户端安装包。
!#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][System.Net.IPAddress]$ServerIp,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$RootThumbprint,
    [switch]$CreateRoot
)
$ErrorActionPreference = 'Stop'
if ($CreateRoot -and $RootThumbprint) { throw '不能同时创建和指定根证书。' }
if (!$CreateRoot -and !$RootThumbprint) { throw '首次显式指定 -CreateRoot；续签必须指定 -RootThumbprint。' }
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
foreach ($name in @('Root.cer','Server.pfx','证书信息.txt')) {
    if (Test-Path -LiteralPath (Join-Path $outputPath $name)) { throw '输出目录已有证书，请为此次签发选择新的目录。' }
}
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
# 密码仅用于加密导出的服务器私钥，不写入命令行或日志。
$password = Read-Host '设置 Server.pfx 导出密码（导入服务器时需要）' -AsSecureString
if ($password.Length -eq 0) { throw '证书导出密码不能为空。' }
if ($CreateRoot) {
    $root = New-SelfSignedCertificate -Type Custom -Subject 'CN=SwiftExport Internal Root CA' `
        -CertStoreLocation 'Cert:\LocalMachine\My' -KeyAlgorithm RSA -KeyLength 3072 `
        -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -KeyUsage CertSign,CRLSign `
        -TextExtension @('2.5.29.19={critical}{text}ca=1&pathlength=0') -NotAfter (Get-Date).AddYears(10)
} else {
    $root = Get-Item -LiteralPath ('Cert:\LocalMachine\My\' + $RootThumbprint)
    if (!$root.HasPrivateKey -or $root.NotAfter -le (Get-Date).AddYears(1)) { throw '根证书缺少私钥或剩余有效期不足一年。' }
}
$server = New-SelfSignedCertificate -Type Custom -Subject ('CN=' + $ServerIp) -Signer $root `
    -CertStoreLocation 'Cert:\LocalMachine\My' -KeyAlgorithm RSA -KeyLength 2048 `
    -HashAlgorithm SHA256 -KeyExportPolicy Exportable -KeyUsage DigitalSignature,KeyEncipherment `
    -TextExtension @('2.5.29.19={text}ca=0','2.5.29.37={text}1.3.6.1.5.5.7.3.1',('2.5.29.17={text}IPAddress=' + $ServerIp)) `
    -NotAfter (Get-Date).AddYears(1)
Export-Certificate -Cert $root -FilePath (Join-Path $outputPath 'Root.cer') | Out-Null
Export-PfxCertificate -Cert $server -FilePath (Join-Path $outputPath 'Server.pfx') -Password $password | Out-Null
@("RootThumbprint=$($root.Thumbprint)","ServerThumbprint=$($server.Thumbprint)","ServerIp=$ServerIp","Expires=$($server.NotAfter.ToString('o'))",
  '续签在持有根私钥的管理员电脑执行，复用 RootThumbprint；不要重新创建根。',
  '根私钥不可导出，请保护此管理环境；环境丢失后需换根并重新部署客户端信任。') | Set-Content -LiteralPath (Join-Path $outputPath '证书信息.txt') -Encoding UTF8
Write-Output "证书已导出到 $outputPath；服务器证书到期前需续签，客户端只接收 Root.cer。"
