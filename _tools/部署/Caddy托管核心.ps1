#Requires -Version 5.1
<#! 同机 Caddy HTTPS 托管的配置、证书导出及无系统信任变更的探测；点号加载不会安装服务。 !#>
. (Join-Path $PSScriptRoot '后端部署核心.ps1')
$script:CaddyServiceName = 'SwiftExport_Caddy'
$script:CaddyBinaryHash = '5F93A9BD555F3AAC9D33AF2756BA14B3519D8CCA4FFD55256BDAEAB444C9DB2A'

# 限定单一 IPv4 入口，防止地址字段成为 Caddyfile 指令；本次不扩展域名和多站点配置。
function Get-CaddyServerAddress([string]$Address) {
    $parsed = $null
    if (![Net.IPAddress]::TryParse($Address, [ref]$parsed) -or $parsed.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or $parsed.ToString() -ne $Address -or $Address -eq '0.0.0.0') { throw '服务器地址须为完整 IPv4 地址。' }
    return $parsed.ToString()
}

# 固定官方二进制校验值；服务器使用离线文件，不从任意 URL 安装可执行程序。
function Get-VerifiedCaddyBinary {
    $exe = Join-Path $PSScriptRoot 'bin/caddy.exe'
    if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw '请先在开发电脑运行 获取Caddy.ps1，并把 bin 文件夹一起复制到服务器。' }
    if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $script:CaddyBinaryHash) { throw 'Caddy 二进制校验失败，请重新运行 获取Caddy.ps1。' }
    return $exe
}

# 生成仅 HTTPS 的同机反向代理；CA 明确落盘、禁用自动安装根证书和管理 API，避免身份/工作目录变化生成不同 CA。
function New-CaddyConfiguration([string]$ServerAddress, [int]$BackendPort, [string]$DataPath, [int]$HttpsPort = 443) {
    $address = Get-CaddyServerAddress $ServerAddress
    if ($BackendPort -lt 1 -or $BackendPort -gt 65535 -or $HttpsPort -lt 1 -or $HttpsPort -gt 65535 -or $HttpsPort -eq $BackendPort) { throw '后端与 HTTPS 端口必须有效且不同。' }
    $data = (Get-DeploymentDirectory $DataPath).Replace('\','/')
    # 路径会出现在双引号配置值中；不允许环境占位符或换行参与配置解析。
    if ($data -match '["{}\r\n]') { throw 'Caddy 数据目录不能包含引号、花括号或换行。' }
    return @"
{
    admin off
    persist_config off
    skip_install_trust
    auto_https disable_redirects
    servers {
        protocols h1 h2
    }
    storage file_system "$data"
    log {
        output file "$data/Logs/caddy.log"
    }
}
https://${address}:$HttpsPort {
    bind $address
    tls internal
    reverse_proxy 127.0.0.1:$BackendPort
}
"@
}

# 只导出 CA 公钥并生成客户端安装入口；证书 PEM 转 DER 以兼容 Windows PowerShell 5.1。
function Export-CaddyRootCertificate([string]$DataPath, [string]$ExportPath) {
    $source = Join-Path $DataPath 'pki/authorities/local/root.crt'
    if (!(Test-Path -LiteralPath $source)) { throw 'Caddy 尚未生成根证书。' }
    $pem = Get-Content -LiteralPath $source -Raw -Encoding ASCII
    $match = [regex]::Match($pem, '-----BEGIN CERTIFICATE-----\s*([A-Za-z0-9+/=\s]+)-----END CERTIFICATE-----')
    if (!$match.Success) { throw 'Caddy 根证书格式无效。' }
    $bytes = [Convert]::FromBase64String(($match.Groups[1].Value -replace '\s',''))
    $certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList (,$bytes)
    try {
        $constraints = $certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.19' }
        if ($certificate.HasPrivateKey -or !$constraints -or !$constraints.CertificateAuthority -or $certificate.Subject -ne $certificate.Issuer) { throw '请选择根 CA 公钥证书。' }
        [void][IO.Directory]::CreateDirectory($ExportPath)
        $rootFile = Join-Path $ExportPath 'SwiftExport_Caddy_Root.cer'
        [IO.File]::WriteAllBytes($rootFile, $certificate.RawData)
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot '安装客户端根证书.ps1') -Destination $ExportPath -Force
        $thumbprint = $certificate.Thumbprint
        $command = "@echo off`r`nchcp 65001 >nul`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0安装客户端根证书.ps1`" -CertificatePath `"%~dp0SwiftExport_Caddy_Root.cer`" -ExpectedThumbprint $thumbprint`r`npause`r`n"
        [IO.File]::WriteAllText((Join-Path $ExportPath '安装此服务器根证书.cmd'), $command, (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText((Join-Path $ExportPath '证书指纹.txt'), "根证书指纹：$thumbprint`r`n有效期至：$($certificate.NotAfter.ToString('yyyy-MM-dd'))`r`n只分发本目录，不要分发 CaddyData 或私钥。`r`n", [Text.Encoding]::UTF8)
        return [PSCustomObject]@{ Path = $rootFile; Thumbprint = $thumbprint }
    } finally { $certificate.Dispose() }
}

# HTTPS 探測使用指定 CA 构建证书链并核对名称；不导入系统证书、不允许任意自签证书。
function Wait-CaddyHttps([string]$Url, [string]$RootCertificatePath) {
    if (!('SwiftExportDeploymentHttpsProbe' -as [type])) {
        Add-Type -IgnoreWarnings -TypeDefinition @'
using System;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.IO;
/// <summary>使用本次导出的根 CA 验证 HTTPS 响应，保留系统信任库。</summary>
public static class SwiftExportDeploymentHttpsProbe {
    /// <summary>验证名称、证书链及后端响应标识，不调用业务或数据库接口。</summary>
    public static bool Check(string url, string rootPath) {
        using (var root = new X509Certificate2(rootPath)) {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Proxy = null;
            request.AllowAutoRedirect = false;
            request.KeepAlive = false;
            request.ConnectionGroupName = Guid.NewGuid().ToString("N");
            request.Timeout = 2000;
            request.ServerCertificateValidationCallback = (sender, cert, chain, errors) => {
                if (cert == null || chain == null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0) return false;
                chain.ChainPolicy.ExtraStore.Add(root);
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                using (var leaf = new X509Certificate2(cert)) {
                    if (!chain.Build(leaf) || chain.ChainElements.Count == 0) return false;
                    return chain.ChainElements[chain.ChainElements.Count - 1].Certificate.Thumbprint == root.Thumbprint;
                }
            };
            HttpWebResponse response;
            try { response = (HttpWebResponse)request.GetResponse(); }
            catch (WebException ex) { response = ex.Response as HttpWebResponse; if (response == null) throw; }
            using (response) {
                var status = (int)response.StatusCode;
                if ((status != 401 && status != 404) || String.IsNullOrEmpty(response.Headers["X-Correlation-ID"])) return false;
                using (var reader = new StreamReader(response.GetResponseStream())) {
                    var body = reader.ReadToEnd();
                    return body.Contains("NOT_FOUND") || body.Contains("UNAUTHENTICATED");
                }
            }
        }
    }
}
'@
    }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try { if ([SwiftExportDeploymentHttpsProbe]::Check($Url, $RootCertificatePath)) { return } } catch { }
        Start-Sleep -Milliseconds 500
    }
    throw 'HTTPS 证书链、名称或后端响应未通过检查；没有跳过 TLS 验证。'
}

# 新建专用服务并收紧 CA 数据 ACL；仅安装固定 Caddy 服务，不接管其他现有服务。
function Install-CaddyService([string]$InstallPath, [string]$DataPath) {
    $exe = Join-Path $InstallPath 'caddy.exe'
    $config = Join-Path $InstallPath 'Caddyfile'
    $binaryPath = '"' + $exe + '" run --config "' + $config + '" --adapter caddyfile'
    New-Service -Name $script:CaddyServiceName -DisplayName 'SwiftExport HTTPS (Caddy)' -BinaryPathName $binaryPath -StartupType Automatic | Out-Null
    $registered = Get-CimInstance Win32_Service -Filter "Name='$script:CaddyServiceName'"
    $account = 'NT SERVICE\' + $script:CaddyServiceName
    # 虚拟账户不能传空密码字符串；必须保留 NULL，让 Windows 管理服务凭据。
    $changed = Invoke-CimMethod -InputObject $registered -MethodName Change -Arguments @{ StartName = $account; StartPassword = $null }
    if ($changed.ReturnValue -ne 0) { throw "Caddy 服务身份设置失败，Windows CIM 返回码：$($changed.ReturnValue)，账户：$account。" }
    & icacls.exe $InstallPath /grant ($account + ':(OI)(CI)RX') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Caddy 程序读取授权失败。' }
    # 此目录为预检确认的独立目录；只给管理员、SYSTEM 和专用服务保留 CA 私钥访问权限。
    & icacls.exe $DataPath /reset | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Caddy 空数据目录 ACL 初始化失败。' }
    & icacls.exe $DataPath /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' ($account + ':(OI)(CI)M') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Caddy CA 目录权限设置失败。' }
    Start-Service -Name $script:CaddyServiceName -ErrorAction Stop
    (Get-Service -Name $script:CaddyServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
}

# 隐藏执行配置解析并捕获原生 stderr；PS 5.1 不把 Caddy 的格式提示误判为安装失败。
function Test-CaddyConfiguration([string]$ExePath, [string]$ConfigurationPath) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $ExePath
    $start.Arguments = 'adapt --config "' + $ConfigurationPath + '" --adapter caddyfile'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(30000)) { $process.Kill(); throw 'Caddy 配置解析超时。' }
        [void]$output.GetAwaiter().GetResult()
        [void]$errors.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw 'Caddy 配置语法检查失败。' }
    } finally { $process.Dispose() }
}
