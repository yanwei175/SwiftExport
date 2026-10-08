#Requires -Version 5.1
#Requires -RunAsAdministrator
<#! 将已由管理员核对指纹的公司根证书安装到计算机信任库；不接收 PFX 或私钥。 !#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CertificatePath,
    [Parameter(Mandatory=$true)][string]$ExpectedThumbprint
)
$ErrorActionPreference = 'Stop'
$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList ([IO.Path]::GetFullPath($CertificatePath))
try {
    if ($certificate.HasPrivateKey -or $certificate.Thumbprint -ne $ExpectedThumbprint.Replace(' ','')) { throw '证书指纹不符或包含私钥，停止安装。' }
    if ($certificate.NotAfter -le (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) { throw '证书不在有效期内。' }
    $constraints = $certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.19' }
    if (!$constraints -or !$constraints.CertificateAuthority -or $certificate.Subject -ne $certificate.Issuer) { throw '请选择内部 CA 根证书，不要将服务器证书加入根信任库。' }
    Import-Certificate -FilePath $CertificatePath -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
    Write-Output '内部根证书已安装；浏览器和使用 Windows 信任库的 WPF 可验证对应服务器证书。'
} finally { $certificate.Dispose() }
