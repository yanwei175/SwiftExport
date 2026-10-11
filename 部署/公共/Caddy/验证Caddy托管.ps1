#Requires -Version 5.1
<#! 真实 Caddy 隔离进程验证 TLS 与 CA 持久化；不注册服务、不导入根证书、不访问业务数据库。 !#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Caddy托管核心.ps1')
$binary = Get-VerifiedCaddyBinary
$workspace = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))) ('artifacts/部署/验证/caddy-tests/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($workspace)

# 无业务的 TCP 上游只返回结构化 404，记录真实 Caddy 转发头；拒绝外部网卡连接。
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
/// <summary>无数据库的本地上游与不写证书存储的测试 CA 工厂。</summary>
public sealed class SwiftExportCaddyTestUpstream : IDisposable {
    private readonly TcpListener listener;
    private readonly Thread thread;
    private volatile bool stopped;
    /// <summary>记录最后一次代理请求的外部协议。</summary>
    public string LastScheme;
    /// <summary>记录最后一次代理请求的客户端地址。</summary>
    public string LastClientAddress;
    /// <summary>返回操作系统分配的独立回环端口。</summary>
    public int Port { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }
    /// <summary>启动仅接受回环的测试上游。</summary>
    public SwiftExportCaddyTestUpstream() {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        thread = new Thread(Serve) { IsBackground = true };
        thread.Start();
    }
    // 按请求读取转发头，始终返回不访问业务的错误响应。
    private void Serve() {
        while (!stopped) {
            try {
                using (var client = listener.AcceptTcpClient())
                using (var stream = client.GetStream()) {
                    stream.ReadTimeout = 3000;
                    var reader = new StreamReader(stream, Encoding.ASCII);
                    string line;
                    while (!String.IsNullOrEmpty(line = reader.ReadLine())) {
                        if (line.StartsWith("X-Forwarded-Proto:", StringComparison.OrdinalIgnoreCase)) LastScheme = line.Substring(18).Trim();
                        if (line.StartsWith("X-Forwarded-For:", StringComparison.OrdinalIgnoreCase)) LastClientAddress = line.Substring(16).Trim();
                    }
                    var body = "{\"error\":{\"code\":\"NOT_FOUND\"}}";
                    var text = "HTTP/1.1 404 Not Found\r\nContent-Type: application/json\r\nX-Correlation-ID: isolated-probe\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\nConnection: close\r\n\r\n" + body;
                    var bytes = Encoding.UTF8.GetBytes(text);
                    stream.Write(bytes, 0, bytes.Length);
                }
            } catch (SocketException) { if (!stopped) throw; }
              catch (ObjectDisposedException) { if (!stopped) throw; }
              catch (IOException) { }
        }
    }
    /// <summary>停止本测试监听器并等待线程退出。</summary>
    public void Dispose() { stopped = true; listener.Stop(); thread.Join(5000); }
    /// <summary>生成另一套临时 CA 公钥，验证错误根证书不能通过检查；不导入系统存储。</summary>
    public static void WriteOtherRoot(string path) {
        using (var key = RSA.Create(2048)) {
            var request = new CertificateRequest("CN=Unrelated isolated CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            using (var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1))) {
                File.WriteAllBytes(path, certificate.Export(X509ContentType.Cert));
            }
        }
    }
}
'@

# 隐藏启动 Caddy 的直接进程，异步排空日志，避免把错误提示当作 PowerShell 异常或堵塞管道。
function Start-IsolatedCaddy([string]$ConfigurationPath) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $binary
    $start.Arguments = 'run --config "' + $ConfigurationPath + '" --adapter caddyfile'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    return [PSCustomObject]@{ Process = $process; Output = $process.StandardOutput.ReadToEndAsync(); Errors = $process.StandardError.ReadToEndAsync() }
}

# 只终止本次持有的隔离实例，保留完整日志，不按进程名批量终止。
function Stop-IsolatedCaddy($Instance,[string]$Label) {
    if ($null -eq $Instance) { return }
    if (!$Instance.Process.HasExited) { $Instance.Process.Kill() }
    [void]$Instance.Process.WaitForExit(10000)
    [IO.File]::WriteAllText((Join-Path $workspace ($Label + '-stderr.log')), $Instance.Errors.GetAwaiter().GetResult())
    [IO.File]::WriteAllText((Join-Path $workspace ($Label + '-stdout.log')), $Instance.Output.GetAwaiter().GetResult())
    $Instance.Process.Dispose()
}
function Assert-CaddyTest([bool]$Condition,[string]$Message) { if (!$Condition) { throw "验证失败：$Message" } }

$upstream = New-Object SwiftExportCaddyTestUpstream
$reservation = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,0)
$reservation.Start()
$port = $reservation.LocalEndpoint.Port
$reservation.Stop()
$data = Join-Path $workspace 'ca'
$config = Join-Path $workspace 'Caddyfile'
$export = Join-Path $workspace 'client-public'
$text = New-CaddyConfiguration '127.0.0.1' $upstream.Port $data $port
[IO.File]::WriteAllText($config,$text,(New-Object Text.UTF8Encoding($false)))
Test-CaddyConfiguration $binary $config
Assert-CaddyTest (!(Test-Path -LiteralPath $data)) '配置解析不能提前生成 CA。'
foreach ($bad in @("127.0.0.1`nfile_server",'0.0.0.0','example.local')) {
    $rejected = $false
    try { New-CaddyConfiguration $bad $upstream.Port $data $port | Out-Null } catch { $rejected = $true }
    Assert-CaddyTest $rejected '非法服务器地址必须被拒绝。'
}
$instance = $null
try {
    $instance = Start-IsolatedCaddy $config
    $rootFile = Join-Path $data 'pki/authorities/local/root.crt'
    for ($attempt = 0; $attempt -lt 80 -and !(Test-Path -LiteralPath $rootFile); $attempt++) {
        if ($instance.Process.HasExited) { throw '隔离 Caddy 提前退出，请检查 artifacts 中的日志。' }
        Start-Sleep -Milliseconds 250
    }
    $root = Export-CaddyRootCertificate $data $export
    foreach ($location in @([Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser,[Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)) {
        $store = New-Object Security.Cryptography.X509Certificates.X509Store('Root',$location)
        try {
            $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
            Assert-CaddyTest ($store.Certificates.Find([Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,$root.Thumbprint,$false).Count -eq 0) '隔离 Caddy 不得向系统或当前用户信任库安装根证书。'
        } finally { $store.Close() }
    }
    $url = 'https://127.0.0.1:' + $port + '/'
    Wait-CaddyHttps $url $root.Path
    Assert-CaddyTest ($upstream.LastScheme -eq 'https' -and $upstream.LastClientAddress -eq '127.0.0.1') 'Caddy 必须转发 HTTPS 协议和客户端地址。'
    Assert-CaddyTest (@(Get-ChildItem -LiteralPath $export -Recurse -Filter '*.key').Count -eq 0) '分发目录不能包含私钥。'
    $wrongRoot = Join-Path $workspace 'unrelated.cer'
    [SwiftExportCaddyTestUpstream]::WriteOtherRoot($wrongRoot)
    $acceptedWrongRoot = $false
    try { $acceptedWrongRoot = [SwiftExportDeploymentHttpsProbe]::Check($url,$wrongRoot) } catch { }
    Assert-CaddyTest (!$acceptedWrongRoot) '不同 CA 的根证书必须无法通过验证。'
    $acceptedWrongName = $false
    try { $acceptedWrongName = [SwiftExportDeploymentHttpsProbe]::Check(('https://localhost:' + $port + '/'),$root.Path) } catch { }
    Assert-CaddyTest (!$acceptedWrongName) '名称不匹配的证书必须无法通过验证。'
    $rootHash = (Get-FileHash -LiteralPath $rootFile).Hash
    $keyHash = (Get-FileHash -LiteralPath (Join-Path $data 'pki/authorities/local/root.key')).Hash
    Stop-IsolatedCaddy $instance 'first'
    $instance = $null
    $instance = Start-IsolatedCaddy $config
    Wait-CaddyHttps $url $root.Path
    Assert-CaddyTest ((Get-FileHash -LiteralPath $rootFile).Hash -eq $rootHash) '重启不能换根证书。'
    Assert-CaddyTest ((Get-FileHash -LiteralPath (Join-Path $data 'pki/authorities/local/root.key')).Hash -eq $keyHash) '重启不能换 CA 私钥。'
    Write-Output '通过：真实 Caddy HTTPS 转发、证书链/名称检查、错误 CA 拒绝、只导出公钥、重启沿用同一 CA。'
} finally {
    Stop-IsolatedCaddy $instance 'final'
    $upstream.Dispose()
}
Write-Output "真实 Caddy 隔离验证完成：$workspace。未安装服务或系统根证书。"
