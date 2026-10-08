#Requires -Version 5.1
<#! 仅在调用部署入口时操作指定程序目录；窗口和离线验证共享更新逻辑，不操作数据库。 !#>
Set-StrictMode -Version 2.0
$script:DeploymentStateName = '.swiftexport-deploy.json'
$script:BackendServiceName = 'SwiftExport_WebApi'

# 校验本地绝对子目录及所有已存在的祖先，拒绝磁盘根目录和链接，保证备份/移动边界可判断。
function Get-DeploymentDirectory([string]$Path) {
    if ($Path -notmatch '^[A-Za-z]:[\\/]') { throw '请选择本地磁盘的绝对子目录。' }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if ($full -eq [IO.Path]::GetPathRoot($full).TrimEnd('\', '/')) { throw '不能使用磁盘根目录。' }
    $cursor = $full
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw '部署路径必须是普通目录，不能经过符号链接或重解析点。'
            }
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    return $full
}

# 在递归复制前拒绝目录内的链接；不把目标外的文件带进备份或新版本。
function Assert-DeploymentTree([string]$Path) {
    if (Get-ChildItem -LiteralPath $Path -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1) {
        throw '程序目录或发布包中包含链接，停止部署。'
    }
}

# 只验证 JSON 格式，不显示配置内容、不连接数据库，也不推导或更改上传目录。
function Assert-ProductionConfiguration([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { throw '首次部署请选择服务器的 appsettings.Production.json。' }
    $configuration = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($null -eq $configuration -or $configuration -is [Array] -or $configuration -is [string]) { throw '生产配置必须是 JSON 对象。' }
    if ($configuration.PSObject.Properties['Auth'] -and $configuration.Auth.PSObject.Properties['UseDevUser'] -and $configuration.Auth.UseDevUser -eq $true) {
        throw '生产配置不能启用 UseDevUser。'
    }
}

# 文件存储位于程序目录之外，防止更新备份移动上传文件；返回显式配置的根目录供服务身份授权。
function Get-DeploymentStorageRoots([string]$ConfigurationPath, [string]$InstallPath) {
    $configuration = Get-Content -LiteralPath $ConfigurationPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (!$configuration.PSObject.Properties['FileStorage']) { return }
    $storage = $configuration.FileStorage
    $roots = @()
    if ($storage.PSObject.Properties['RootPath'] -and $storage.RootPath) { $roots += [string]$storage.RootPath }
    if ($storage.PSObject.Properties['Containers']) {
        foreach ($container in $storage.Containers.PSObject.Properties) {
            if ($container.Value.PSObject.Properties['RootPath'] -and $container.Value.RootPath) { $roots += [string]$container.Value.RootPath }
        }
    }
    foreach ($root in $roots | Select-Object -Unique) {
        $full = Get-DeploymentDirectory $root
        if ($full -eq $InstallPath -or $full.StartsWith($InstallPath + '\', [StringComparison]::OrdinalIgnoreCase) -or $InstallPath.StartsWith($full + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw '文件存储和程序安装目录必须相互独立；请先单独迁移上传目录。'
        }
        if (!(Test-Path -LiteralPath $full -PathType Container)) { throw '请先创建生产配置中的文件存储及容器根目录。' }
        Write-Output $full
    }
}

# 读取工具记录的运行方式和进程身份；更新不会按进程名批量结束其他后端。
function Get-BackendDeploymentState([string]$InstallPath) {
    $path = Join-Path $InstallPath $script:DeploymentStateName
    if (Test-Path -LiteralPath $path -PathType Leaf) { return Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json }
    return $null
}

# 验证 PID、启动时间及可执行文件路径三者一致，防止 PID 重用导致误停其他程序。
function Get-OwnedBackendProcess($State, [string]$InstallPath) {
    if ($null -eq $State -or $State.Mode -ne 'Process') { return $null }
    $process = Get-Process -Id $State.ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $null }
    $expected = Join-Path $InstallPath 'SwiftExport.WebApi.exe'
    if (![string]::Equals($process.Path, $expected, [StringComparison]::OrdinalIgnoreCase) -or $process.StartTime.ToUniversalTime().Ticks -ne $State.StartTicks) {
        throw '进程身份与部署记录不一致，不能自动结束该进程。'
    }
    return $process
}

# 服务注册及 ACL 更新需要管理员令牌；直接运行模式沿用当前普通账户权限。
function Assert-BackendPrivileges([string]$Mode) {
    if ($Mode -eq 'Service') {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Windows 服务模式请以管理员身份运行部署工具。' }
    }
}

# 部署前检查运行所有权与服务绑定；首次接管已有目录须由用户先停止未受工具管理的进程。
function Assert-BackendOwnership([string]$InstallPath, [string]$Mode, $State) {
    if ($null -ne $State -and $State.Mode -ne $Mode) { throw '不能在更新中切换运行方式，请先单独迁移。' }
    $expected = Join-Path $InstallPath 'SwiftExport.WebApi.exe'
    $service = Get-CimInstance Win32_Service -Filter "Name='$script:BackendServiceName'"
    if ($null -ne $service) {
        if ($Mode -ne 'Service') { throw '此后端已有 Windows 服务，请选择 Windows 服务模式。' }
        if (!$service.PathName.StartsWith('"' + $expected + '" ', [StringComparison]::OrdinalIgnoreCase)) {
            throw '同名 Windows 服务并未绑定目标程序目录，不能接管。'
        }
    }
    if ($Mode -eq 'Process') {
        $owned = Get-OwnedBackendProcess $State $InstallPath
        foreach ($process in @(Get-Process -Name 'SwiftExport.WebApi' -ErrorAction SilentlyContinue)) {
            if ([string]::Equals($process.Path, $expected, [StringComparison]::OrdinalIgnoreCase) -and ($null -eq $owned -or $process.Id -ne $owned.Id)) {
                throw '目标目录已有未受工具管理的运行进程，请先停止它。'
            }
        }
    }
}

# 服务模式使用 SCM 的正常停止；普通进程只终止此前已校验且由本工具记录的实例。
function Stop-BackendInstance([string]$InstallPath, [string]$Mode, $State) {
    if ($Mode -eq 'Service') {
        $service = Get-Service -Name $script:BackendServiceName -ErrorAction SilentlyContinue
        if ($null -ne $service -and $service.Status -ne 'Stopped') {
            Stop-Service -Name $script:BackendServiceName -ErrorAction Stop
            $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        }
    } else {
        $process = Get-OwnedBackendProcess $State $InstallPath
        if ($null -ne $process) {
            # 通过已校验实例的句柄终止进程，避免校验后按 PID 再查询遇到重用。
            if (!$process.HasExited) { $process.Kill() }
            if (!$process.WaitForExit(30000)) { throw '旧进程未能退出，停止更新。' }
        }
    }
}

# 隐藏启动普通后端，或安装/更新固定名称的 Windows 服务；生产环境及监听地址显式传入。
function Start-BackendInstance([string]$InstallPath, [string]$Mode, [string]$ListenUrl) {
    $exe = Join-Path $InstallPath 'SwiftExport.WebApi.exe'
    $arguments = '--environment Production --urls "' + $ListenUrl + '"'
    if ($Mode -eq 'Service') {
        $binaryPath = '"' + $exe + '" ' + $arguments
        $service = Get-Service -Name $script:BackendServiceName -ErrorAction SilentlyContinue
        if ($null -eq $service) {
            New-Service -Name $script:BackendServiceName -DisplayName 'SwiftExport 后端' -BinaryPathName $binaryPath -StartupType Automatic | Out-Null
            # 使用本服务的虚拟账户，不让后端默认以 LocalSystem 管理员身份运行。
            $registered = Get-CimInstance Win32_Service -Filter "Name='$script:BackendServiceName'"
            $changed = Invoke-CimMethod -InputObject $registered -MethodName Change -Arguments @{
                StartName = 'NT SERVICE\' + $script:BackendServiceName
                StartPassword = ''
            }
            if ($changed.ReturnValue -ne 0) { throw '服务运行身份设置失败。' }
        } else {
            # 使用结构化 CIM 参数保留带空格程序路径的引号，避免外部命令行二次解析造成路径错误。
            $registered = Get-CimInstance Win32_Service -Filter "Name='$script:BackendServiceName'"
            $changed = Invoke-CimMethod -InputObject $registered -MethodName Change -Arguments @{ PathName = $binaryPath }
            if ($changed.ReturnValue -ne 0) { throw 'Windows 服务启动参数更新失败。' }
        }
        [void][IO.Directory]::CreateDirectory((Join-Path $InstallPath 'Logs'))
        $account = 'NT SERVICE\' + $script:BackendServiceName
        & icacls.exe $InstallPath /grant ($account + ':(OI)(CI)RX') | Out-Null
        if ($LASTEXITCODE -ne 0) { throw '服务程序读取权限设置失败。' }
        & icacls.exe (Join-Path $InstallPath 'Logs') /grant ($account + ':(OI)(CI)M') | Out-Null
        if ($LASTEXITCODE -ne 0) { throw '服务日志写入权限设置失败。' }
        foreach ($root in @(Get-DeploymentStorageRoots (Join-Path $InstallPath 'appsettings.Production.json') $InstallPath)) {
            & icacls.exe $root /grant ($account + ':(OI)(CI)M') | Out-Null
            if ($LASTEXITCODE -ne 0) { throw '服务文件存储权限设置失败。' }
        }
        Start-Service -Name $script:BackendServiceName -ErrorAction Stop
        (Get-Service -Name $script:BackendServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        $state = @{ Mode = $Mode; ListenUrl = $ListenUrl }
    } else {
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.FileName = $exe
        $start.WorkingDirectory = $InstallPath
        $start.Arguments = $arguments
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Production'
        $start.EnvironmentVariables['DOTNET_ENVIRONMENT'] = 'Production'
        $process = [Diagnostics.Process]::Start($start)
        $state = @{ Mode = $Mode; ListenUrl = $ListenUrl; ProcessId = $process.Id; StartTicks = $process.StartTime.ToUniversalTime().Ticks }
    }
    try {
        $state | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $InstallPath $script:DeploymentStateName) -Encoding UTF8
    } catch {
        # 启动记录失败时立即结束本次刚创建的实例，不能留下无法安全识别的后台进程。
        if ($Mode -eq 'Process' -and !$process.HasExited) { $process.Kill(); [void]$process.WaitForExit(30000) }
        if ($Mode -eq 'Service') { Stop-Service -Name $script:BackendServiceName -ErrorAction Stop }
        throw
    }
    return [PSCustomObject]$state
}

# 只请求根路径，确认应用的相关标识与进程存活；不调用认证、产品或其他数据库接口。
function Wait-BackendReady([string]$InstallPath, [string]$Mode, $State, [string]$ListenUrl) {
    $uri = New-Object Uri($ListenUrl)
    $hostName = if ($uri.Host -eq '0.0.0.0') { '127.0.0.1' } else { $uri.Host }
    $probe = '{0}://{1}:{2}/' -f $uri.Scheme, $hostName, $uri.Port
    Add-Type -AssemblyName System.Net.Http
    # 不跟随 HTTPS 跳转；启动检查允许现有 HTTPS 策略返回跳转，但不替代证书和客户端连接验收。
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $false
    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(2)
    try {
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            if ($Mode -eq 'Process' -and $null -eq (Get-OwnedBackendProcess $State $InstallPath)) { throw '新后端进程已退出，请检查 Logs。' }
            if ($Mode -eq 'Service' -and (Get-Service -Name $script:BackendServiceName).Status -ne 'Running') { throw '新后端服务已停止，请检查 Logs。' }
            try {
                $response = $client.GetAsync($probe).GetAwaiter().GetResult()
                try {
                    if ([int]$response.StatusCode -in @(307,308) -and $response.Headers.Contains('X-Correlation-ID')) { return }
                    if ([int]$response.StatusCode -in @(401,404) -and $response.Headers.Contains('X-Correlation-ID')) {
                        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
                        if ($body.PSObject.Properties['Error'] -and $body.Error.Code -in @('NOT_FOUND','UNAUTHENTICATED')) { return }
                    }
                } finally { $response.Dispose() }
            } catch { }
            Start-Sleep -Milliseconds 500
        }
        throw '新版本没有通过启动检查；数据库及业务功能需另行验收。'
    } finally { $client.Dispose() }
}

# 先准备新目录再停止旧版本；完整备份旧目录，失败保留新目录并恢复旧目录，不递归删除任何目录。
function Invoke-BackendDeployment {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)][string]$PackagePath,
        [Parameter(Mandatory=$true)][string]$InstallPath,
        [ValidateSet('Process','Service')][string]$Mode = 'Process',
        [string]$ListenUrl = 'http://127.0.0.1:5080',
        [string]$ProductionConfigurationPath,
        [scriptblock]$Progress = { param($message) Write-Host $message }
    )
    $package = Get-DeploymentDirectory $PackagePath
    $target = Get-DeploymentDirectory $InstallPath
    if ($package -eq $target -or $package.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase) -or $target.StartsWith($package + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '发布目录和安装目录不能相同或互相包含。' }
    $listen = $null
    if (![Uri]::TryCreate($ListenUrl, [UriKind]::Absolute, [ref]$listen) -or $listen.Scheme -ne 'http' -or $listen.Host -notin @('127.0.0.1','localhost','0.0.0.0') -or $listen.AbsolutePath -ne '/' -or $listen.Query -or $listen.Fragment -or $listen.UserInfo) { throw '监听地址须为 http://127.0.0.1:端口 或 http://0.0.0.0:端口。' }
    Assert-BackendPrivileges $Mode
    foreach ($name in @('SwiftExport.WebApi.exe','SwiftExport.WebApi.dll','SwiftExport.WebApi.deps.json','SwiftExport.WebApi.runtimeconfig.json')) {
        if (!(Test-Path -LiteralPath (Join-Path $package $name) -PathType Leaf)) { throw "发布目录缺少 $name，请选择完整 Windows 发布目录。" }
    }
    if ($Mode -eq 'Service' -and !(Test-Path -LiteralPath (Join-Path $package 'Microsoft.Extensions.Hosting.WindowsServices.dll'))) { throw '发布包尚未包含 Windows 服务托管支持，请重新发布本次新版后端。' }
    Assert-DeploymentTree $package
    $parent = Split-Path $target -Parent
    [void][IO.Directory]::CreateDirectory($parent)
    $suffix = (Get-Date -Format 'yyyyMMdd_HHmmss') + '_' + [Guid]::NewGuid().ToString('N').Substring(0,8)
    $stage = $target + '_new_' + $suffix
    $backup = $target + '_backup_' + $suffix
    $failed = $target + '_failed_' + $suffix
    # 所有移动目的地都在刚验证的同一父目录内，且使用本次唯一后缀；不接管已有备份。
    foreach ($path in @($stage,$backup,$failed)) {
        if ((Split-Path $path -Parent) -ne $parent -or (Test-Path -LiteralPath $path)) { throw '本次暂存或备份路径冲突。' }
    }
    $lockPath = $target + '.deploy.lock'
    $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $switched = $false
    $oldStopped = $false
    $oldExists = $false
    $oldState = $null
    $newState = $null
    $serviceWasPresent = $true
    $oldService = $null
    $oldServiceWasRunning = $false
    try {
        # 获取同目标排他锁后再读取旧目录/进程状态，避免另一窗口完成更新后仍使用过期 PID。
        $oldExists = Test-Path -LiteralPath $target
        $isUpgrade = $oldExists -and [bool](Get-ChildItem -LiteralPath $target -Force | Select-Object -First 1)
        if ($oldExists) { Assert-DeploymentTree $target }
        if ($isUpgrade) {
            if ($ProductionConfigurationPath) { throw '更新自动保留服务器配置，请清空首次部署配置选择。' }
            $production = Join-Path $target 'appsettings.Production.json'
        } else { $production = $ProductionConfigurationPath }
        Assert-ProductionConfiguration $production
        [void]@(Get-DeploymentStorageRoots $production $target)
        $oldState = Get-BackendDeploymentState $target
        Assert-BackendOwnership $target $Mode $oldState
        if ($Mode -eq 'Service') {
            $service = Get-Service -Name $script:BackendServiceName -ErrorAction SilentlyContinue
            $serviceWasPresent = $null -ne $service
            if ($serviceWasPresent) {
                $oldService = Get-CimInstance Win32_Service -Filter "Name='$script:BackendServiceName'"
                $oldServiceWasRunning = $service.Status -eq 'Running'
            }
        }
        & $Progress '正在准备新版本，旧程序此时仍可运行……'
        [void][IO.Directory]::CreateDirectory($stage)
        foreach ($item in Get-ChildItem -LiteralPath $package -Force) {
            if ($item.Name -like 'appsettings*.json' -or $item.Name -in @('Logs','app_offline.htm',$script:DeploymentStateName)) { continue }
            Copy-Item -LiteralPath $item.FullName -Destination $stage -Recurse -Force
        }
        Set-Content -LiteralPath (Join-Path $stage 'appsettings.json') -Value '{}' -Encoding UTF8
        if ($isUpgrade) {
            foreach ($file in Get-ChildItem -LiteralPath $target -Filter 'appsettings*.json' -File) {
                Copy-Item -LiteralPath $file.FullName -Destination $stage
                Set-Acl -LiteralPath (Join-Path $stage $file.Name) -AclObject (Get-Acl -LiteralPath $file.FullName)
            }
        } else { Copy-Item -LiteralPath $production -Destination (Join-Path $stage 'appsettings.Production.json') }
        & $Progress '正在停止旧程序并备份；维护期间请勿保存或提交单据……'
        Stop-BackendInstance $target $Mode $oldState
        $oldStopped = $true
        if ($oldExists) {
            if (Test-Path -LiteralPath (Join-Path $target 'Logs')) {
                Copy-Item -LiteralPath (Join-Path $target 'Logs') -Destination $stage -Recurse
                Set-Acl -LiteralPath (Join-Path $stage 'Logs') -AclObject (Get-Acl -LiteralPath (Join-Path $target 'Logs'))
            }
            Set-Acl -LiteralPath $stage -AclObject (Get-Acl -LiteralPath $target)
            Move-Item -LiteralPath $target -Destination $backup
        }
        Move-Item -LiteralPath $stage -Destination $target
        $switched = $true
        & $Progress '正在启动新版本并检查 HTTP 管道……'
        $newState = Start-BackendInstance $target $Mode $ListenUrl
        Wait-BackendReady $target $Mode $newState $ListenUrl
        & $Progress '部署成功。请再验证登录和本次功能。'
        return [PSCustomObject]@{ InstallPath = $target; BackupPath = $(if ($oldExists) { $backup } else { $null }); Mode = $Mode }
    } catch {
        $deploymentError = $_.Exception.Message
        try {
            if ($switched) {
                if ($null -eq $newState) { $newState = Get-BackendDeploymentState $target }
                Stop-BackendInstance $target $Mode $newState
                Move-Item -LiteralPath $target -Destination $failed
            }
            if ($oldStopped -and $oldExists) {
                if (Test-Path -LiteralPath $backup) { Move-Item -LiteralPath $backup -Destination $target }
                if ($Mode -eq 'Service' -and $null -ne $oldService) {
                    # 已有服务即使此前没有工具状态文件，也恢复原完整命令行及运行状态，不猜测旧监听参数。
                    $restoredService = Invoke-CimMethod -InputObject $oldService -MethodName Change -Arguments @{ PathName = $oldService.PathName }
                    if ($restoredService.ReturnValue -ne 0) { throw '旧服务启动参数恢复失败。' }
                    if ($oldServiceWasRunning) {
                        Start-Service -Name $script:BackendServiceName -ErrorAction Stop
                        (Get-Service -Name $script:BackendServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
                        if ($null -ne $oldState) { Wait-BackendReady $target $Mode $oldState $oldState.ListenUrl }
                    }
                } elseif ($Mode -eq 'Process' -and $null -ne $oldState) {
                    $restored = Start-BackendInstance $target $oldState.Mode $oldState.ListenUrl
                    Wait-BackendReady $target $oldState.Mode $restored $oldState.ListenUrl
                }
                & $Progress '更新失败，已恢复旧程序目录；新版本暂存/失败目录保留用于排查。'
            }
            if ($Mode -eq 'Service' -and !$serviceWasPresent -and $null -ne (Get-Service -Name $script:BackendServiceName -ErrorAction SilentlyContinue)) {
                # 仅撤销本次首次创建的固定服务，绝不删除部署前已有的服务。
                & sc.exe delete $script:BackendServiceName | Out-Null
                if ($LASTEXITCODE -ne 0) { throw '本次新建服务未能撤销。' }
            }
        } catch { throw "更新失败：$deploymentError；恢复也失败：$($_.Exception.Message)。请保留目录并人工检查。" }
        throw "部署失败：$deploymentError。未删除备份、失败目录或文件存储。"
    } finally { $lock.Dispose() }
}
