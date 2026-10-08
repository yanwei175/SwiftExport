#Requires -Version 5.1
<#! 首次托管的预检与切换事务；加载本文件不安装服务，供窗口及隔离验证复用。 !#>
. (Join-Path $PSScriptRoot 'Caddy托管核心.ps1')
# 目录必须彼此独立，既不覆盖旧 IIS 文件，也不允许把 CA 或图片放进程序更新范围。
function Assert-IndependentHostingPaths([string[]]$Paths) {
    for ($i = 0; $i -lt $Paths.Count; $i++) {
        for ($j = $i + 1; $j -lt $Paths.Count; $j++) {
            if ($Paths[$i] -eq $Paths[$j] -or $Paths[$i].StartsWith($Paths[$j] + '\', [StringComparison]::OrdinalIgnoreCase) -or $Paths[$j].StartsWith($Paths[$i] + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '发布包、部署工具、旧 IIS、安装目录、CA 与证书导出目录必须相互独立。' }
        }
    }
}

# 记录目标站点/应用池状态；共享应用池不自动停止，其他 IIS 网站也不参与迁移。
function Get-IisCutoverState([string]$Name, [string]$Address) {
    Import-Module WebAdministration -ErrorAction Stop
    $sites = @(Get-Website -Name $Name | Where-Object { $_.Name -eq $Name })
    if ($sites.Count -ne 1) { throw '找不到唯一指定 IIS 站点；新服务器请选择不迁移 IIS。' }
    $site = $sites[0]
    $pool = [string]$site.applicationPool
    $sharing = @(Get-Website | Where-Object { $_.applicationPool -eq $pool -and $_.Name -ne $Name })
    $applications = @(Get-WebApplication | Where-Object { $_.applicationPool -eq $pool })
    if ($sharing.Count -gt 0 -or $applications.Count -gt 0) { throw '目标应用池被其他网站或子应用使用，不能自动停止；请先为测试站点分配独立应用池。' }
    foreach ($other in @(Get-Website | Where-Object { $_.Name -ne $Name -and ($_.State -eq 'Started' -or $_.serverAutoStart) })) {
        foreach ($binding in $other.bindings.Collection) {
            if ($binding.protocol -eq 'https' -and $binding.bindingInformation -match '^([^:]*):443:' -and $Matches[1] -in @('*','',$Address,'0.0.0.0')) { throw '其他 IIS 站点占用目标 HTTPS 地址，停止迁移。' }
        }
    }
    return [PSCustomObject]@{
        SiteName = $Name; PoolName = $pool
        SiteWasStarted = $site.State -eq 'Started'
        PoolWasStarted = (Get-WebAppPoolState -Name $pool).Value -eq 'Started'
        SiteAutoStart = [bool]$site.serverAutoStart
        PoolAutoStart = [bool](Get-ItemProperty -LiteralPath ('IIS:\AppPools\' + $pool) -Name autoStart).autoStart
        PhysicalPath = Get-DeploymentDirectory ([Environment]::ExpandEnvironmentVariables($site.physicalPath))
    }
}

# 先完成预检，再停止指定 IIS；失败只撤销本次新建服务并恢复旧站点运行状态。
function Invoke-CaddyFirstInstallation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)][string]$PackagePath,
        [Parameter(Mandatory=$true)][string]$ProductionConfigurationPath,
        [string]$ServerAddress = '192.168.3.12',
        [string]$BackendInstallPath = 'D:\Apps\SwiftExport_Service',
        [string]$CaddyInstallPath = 'D:\Apps\SwiftExport_Caddy',
        [string]$CaddyDataPath = 'D:\ERPDeploy\CaddyData',
        [string]$CertificateExportPath = 'D:\ERPDeploy\客户端证书',
        [ValidateRange(1024,65535)][int]$BackendPort = 5081,
        [string]$IisSiteName = 'SwiftExport_WebApi',
        [switch]$NoIis,
        [switch]$PlanOnly,
        [scriptblock]$Progress = { param($message) Write-Host $message }
    )
    $ErrorActionPreference = 'Stop'
    Assert-BackendPrivileges 'Service'
    $address = Get-CaddyServerAddress $ServerAddress
    $package = Get-DeploymentDirectory $PackagePath
    $backend = Get-DeploymentDirectory $BackendInstallPath
    $caddy = Get-DeploymentDirectory $CaddyInstallPath
    $data = Get-DeploymentDirectory $CaddyDataPath
    $export = Get-DeploymentDirectory $CertificateExportPath
    $tool = Get-DeploymentDirectory $PSScriptRoot
    $binary = Get-VerifiedCaddyBinary
    Assert-ProductionConfiguration $ProductionConfigurationPath
    $configuration = Get-Content -LiteralPath $ProductionConfigurationPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (!$configuration.PSObject.Properties['ConnectionStrings'] -or !$configuration.ConnectionStrings.PSObject.Properties['Default'] -or $configuration.ConnectionStrings.Default -notmatch '(?i)(^|;)\s*Host\s*=') { throw '请选择包含现有 PostgreSQL Host 连接配置的生产 JSON；不沿用旧 SQL Server 配置。' }
    if (!$configuration.PSObject.Properties['Auth'] -or !$configuration.Auth.PSObject.Properties['JwtKey'] -or [Text.Encoding]::UTF8.GetByteCount([string]$configuration.Auth.JwtKey) -lt 32) { throw '生产配置必须保留现有至少 32 字节的 JWT 密钥。' }
    # 显式 Kestrel 端点优先于 --urls，必须在停止 IIS 前拒绝旧 HTTPS/公网监听，不能静默覆盖或沿用。
    if ($configuration.PSObject.Properties['Kestrel'] -and $configuration.Kestrel.PSObject.Properties['Endpoints'] -and @($configuration.Kestrel.Endpoints.PSObject.Properties).Count -gt 0) { throw '生产配置含显式 Kestrel:Endpoints；请先移除旧端点配置，由工具指定回环监听。' }
    foreach ($name in @('SwiftExport.WebApi.exe','SwiftExport.WebApi.dll','SwiftExport.WebApi.deps.json','SwiftExport.WebApi.runtimeconfig.json','Microsoft.Extensions.Hosting.WindowsServices.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $package $name) -PathType Leaf)) { throw "Windows 发布目录缺少 $name，请重新发布当前版本。" }
    }
    Assert-DeploymentTree $package
    if (!(Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -eq $address })) { throw '目标 IP 不属于此服务器，请核对服务器地址。' }
    foreach ($name in @($script:BackendServiceName,$script:CaddyServiceName)) {
        if (Get-Service -Name $name -ErrorAction SilentlyContinue) { throw '已存在同名服务；首次安装工具不重复安装，后续请用后端部署窗口更新。' }
    }
    $firewallName = 'SwiftExport_Caddy_HTTPS'
    if (Get-NetFirewallRule -Name $firewallName -ErrorAction SilentlyContinue) { throw '存在同名防火墙规则，请先核对上次安装结果。' }
    if (Get-NetTCPConnection -State Listen -LocalPort $BackendPort -ErrorAction SilentlyContinue) { throw '后端回环端口已被占用。' }
    $iis = if (!$NoIis) { Get-IisCutoverState $IisSiteName $address } else { $null }
    $paths = @($package,$backend,$caddy,$data,$export,$tool)
    if ($null -ne $iis) { $paths += $iis.PhysicalPath }
    Assert-IndependentHostingPaths $paths
    foreach ($root in @(Get-DeploymentStorageRoots $ProductionConfigurationPath $backend)) {
        foreach ($path in $paths) { Assert-IndependentHostingPaths @($path,$root) }
    }
    foreach ($path in @($backend,$caddy,$data,$export)) {
        if ((Test-Path -LiteralPath $path) -and (Get-ChildItem -LiteralPath $path -Force | Select-Object -First 1)) { throw '首次安装的目标目录须为空，不能接管已有程序或 CA；请核对目录和此前失败记录。' }
    }
    if ($NoIis -and (Get-NetTCPConnection -State Listen -LocalPort 443 -ErrorAction SilentlyContinue | Where-Object { $_.LocalAddress -in @($address,'0.0.0.0','::') })) { throw 'HTTPS 端口已被占用。' }
    $listenUrl = 'http://127.0.0.1:' + $BackendPort
    $url = 'https://' + $address + '/'
    $caddyText = New-CaddyConfiguration $address $BackendPort $data
    & $Progress "预检通过：HTTPS $url -> $listenUrl；不修改数据库，不移动图片。"
    if ($PlanOnly) {
        return [PSCustomObject]@{ BackendInstallPath = $backend; CaddyInstallPath = $caddy; CaddyDataPath = $data; CertificateExportPath = $export; HttpsUrl = $url; ListenUrl = $listenUrl; IisSite = $iis }
    }

    # 两项服务使用固定名称，即使选了不同安装目录也只允许一个首次安装；锁失败不停止 IIS。
    $mutex = New-Object Threading.Mutex($false, 'Global\SwiftExport_Caddy_FirstInstallation')
    $acquired = $false
    try { $acquired = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $acquired = $true }
    if (!$acquired) { $mutex.Dispose(); throw '另一个首次安装正在执行，请等它结束后核对结果。' }
    $lock = $null
    $iisStopped = $false
    $backendInstalled = $false
    $caddyAttempted = $false
    $firewallCreated = $false
    try {
        [void][IO.Directory]::CreateDirectory((Split-Path $caddy -Parent))
        $lock = [IO.File]::Open(($caddy + '.install.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        # 排他锁下再次核对服务/目录，避免两个首次安装窗口使用过期的空目录判定。
        foreach ($name in @($script:BackendServiceName,$script:CaddyServiceName)) {
            if (Get-Service -Name $name -ErrorAction SilentlyContinue) { throw '另一安装已创建同名服务，停止重复操作。' }
        }
        foreach ($path in @($backend,$caddy,$data,$export)) {
            if ((Test-Path -LiteralPath $path) -and (Get-ChildItem -LiteralPath $path -Force | Select-Object -First 1)) { throw '目录状态已变化，停止重复安装。' }
        }
        [void][IO.Directory]::CreateDirectory($caddy)
        [void][IO.Directory]::CreateDirectory($data)
        Copy-Item -LiteralPath $binary -Destination (Join-Path $caddy 'caddy.exe')
        [IO.File]::WriteAllText((Join-Path $caddy 'Caddyfile'), $caddyText, (New-Object Text.UTF8Encoding($false)))
        # adapt 只解析配置；validate 会触发 CA 初始化，不能在管理员身份预先生成另一套 CA。
        Test-CaddyConfiguration (Join-Path $caddy 'caddy.exe') (Join-Path $caddy 'Caddyfile')
        if ($null -ne $iis) {
            $iis | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $caddy 'IIS切换前状态.json') -Encoding UTF8
            $iisStopped = $true
            # 停止状态不能防止重启后 IIS 再次抢占端口；仅关闭指定测试站点/池的自动启动并记录原值。
            Set-ItemProperty -LiteralPath ('IIS:\Sites\' + $iis.SiteName) -Name serverAutoStart -Value $false
            Set-ItemProperty -LiteralPath ('IIS:\AppPools\' + $iis.PoolName) -Name autoStart -Value $false
            if ($iis.SiteWasStarted) { Stop-Website -Name $iis.SiteName }
            if ($iis.PoolWasStarted) { Stop-WebAppPool -Name $iis.PoolName }
            & $Progress '指定 IIS 测试站点已停止；旧站点配置、证书及文件均保留。'
        }
        $result = Invoke-BackendDeployment -PackagePath $package -InstallPath $backend -Mode Service -ListenUrl $listenUrl -ProductionConfigurationPath $ProductionConfigurationPath -Progress $Progress
        $backendInstalled = $true
        $caddyAttempted = $true
        Install-CaddyService $caddy $data
        $rootPath = Join-Path $data 'pki/authorities/local/root.crt'
        for ($attempt = 0; $attempt -lt 40 -and !(Test-Path -LiteralPath $rootPath); $attempt++) { Start-Sleep -Milliseconds 250 }
        $root = Export-CaddyRootCertificate $data $export
        Wait-CaddyHttps $url $root.Path
        # 只新增本工具的 HTTPS 入站规则；不放开后端端口，也不修改其他现有防火墙规则。
        New-NetFirewallRule -Name $firewallName -DisplayName 'SwiftExport Caddy 内网 HTTPS' -Direction Inbound -Action Allow -Protocol TCP -LocalAddress $address -LocalPort 443 -RemoteAddress LocalSubnet -Profile Domain,Private -ErrorAction Stop | Out-Null
        $firewallCreated = $true
        $state = [PSCustomObject]@{ BackendInstallPath = $backend; ListenUrl = $listenUrl; CaddyInstallPath = $caddy; CaddyDataPath = $data; CertificateExportPath = $export; HttpsUrl = $url; RootThumbprint = $root.Thumbprint; FirewallRule = $firewallName; Iis = $iis }
        $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $caddy '托管状态.json') -Encoding UTF8
        & $Progress "HTTPS 已通过证书链和后端响应检查。客户端证书包：$export；根证书指纹：$($root.Thumbprint)"
        return $state
    } catch {
        $failure = $_.Exception.Message
        try {
            if ($firewallCreated) { Remove-NetFirewallRule -Name $firewallName -ErrorAction Stop }
            if ($caddyAttempted -and (Get-Service -Name $script:CaddyServiceName -ErrorAction SilentlyContinue)) {
                Stop-Service -Name $script:CaddyServiceName -ErrorAction Stop
                (Get-Service -Name $script:CaddyServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
                & sc.exe delete $script:CaddyServiceName | Out-Null
                if ($LASTEXITCODE -ne 0) { throw '新建 Caddy 服务撤销失败。' }
            }
            if ($backendInstalled) {
                Stop-BackendInstance $backend 'Service' (Get-BackendDeploymentState $backend)
                & sc.exe delete $script:BackendServiceName | Out-Null
                if ($LASTEXITCODE -ne 0) { throw '新建后端服务撤销失败。' }
            }
            if ($iisStopped) {
                Set-ItemProperty -LiteralPath ('IIS:\AppPools\' + $iis.PoolName) -Name autoStart -Value $iis.PoolAutoStart
                Set-ItemProperty -LiteralPath ('IIS:\Sites\' + $iis.SiteName) -Name serverAutoStart -Value $iis.SiteAutoStart
                if ($iis.PoolWasStarted) { Start-WebAppPool -Name $iis.PoolName }
                if ($iis.SiteWasStarted) { Start-Website -Name $iis.SiteName }
            }
            if ($iisStopped) { & $Progress '本次新建服务已撤销，旧 IIS 原运行状态已恢复；所有目录和 CA 文件保留，请先排查失败原因。' }
            else { & $Progress '本次新建服务已撤销；此次未迁移或恢复 IIS。所有目录和 CA 文件保留，请先排查失败原因。' }
        } catch { throw "首次安装失败：$failure；恢复也失败：$($_.Exception.Message)。请人工检查服务和 IIS。" }
        throw "首次安装未完成：$failure。失败目录保留，重新安装前请核对并选择新的空目录；不能重建已经分发的 CA。"
    } finally {
        if ($null -ne $lock) { $lock.Dispose() }
        $mutex.ReleaseMutex()
        $mutex.Dispose()
    }
}
