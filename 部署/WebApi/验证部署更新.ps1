#Requires -Version 5.1
<#! 在仓库 artifacts 隔离目录验证部署文件操作；进程、服务和网络全部使用替身，不操作现有服务器。 !#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '后端部署核心.ps1')
$realOwnershipCheck = (Get-Command Assert-BackendOwnership).ScriptBlock
$realReadinessCheck = (Get-Command Wait-BackendReady).ScriptBlock
$realProcessOwnership = (Get-Command Get-OwnedBackendProcess).ScriptBlock
$realStartInstance = (Get-Command Start-BackendInstance).ScriptBlock
$realStopInstance = (Get-Command Stop-BackendInstance).ScriptBlock
$workspace = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) ('artifacts/部署/验证/deployment-tests/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($workspace)
$script:StartCount = 0
$script:StopCount = 0
$script:FailFirstReadiness = $false

# 测试替身负责隔离所有宿主副作用，同时写入真实部署状态以验证恢复流程。
function Assert-BackendOwnership([string]$InstallPath, [string]$Mode, $State) { }
function Stop-BackendInstance([string]$InstallPath, [string]$Mode, $State) { $script:StopCount++ }
function Start-BackendInstance([string]$InstallPath, [string]$Mode, [string]$ListenUrl) {
    $script:StartCount++
    $state = [PSCustomObject]@{ Mode = $Mode; ListenUrl = $ListenUrl; ProcessId = 123; StartTicks = 456 }
    $state | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $InstallPath $script:DeploymentStateName) -Encoding UTF8
    return $state
}
function Wait-BackendReady([string]$InstallPath, [string]$Mode, $State, [string]$ListenUrl) {
    if ($script:FailFirstReadiness) { $script:FailFirstReadiness = $false; throw '模拟启动失败' }
}

# 服务用例采用 SCM/CIM 替身；不会注册服务、设置 ACL 或要求管理员令牌。
function Assert-BackendPrivileges([string]$Mode) { }
$script:FakeService = $null
$script:FakeServiceInfo = $null
$script:RestoredServiceCommand = $null
$script:RestoredServiceStarts = 0
function Get-Service { param([string]$Name, [string]$ErrorAction); return $script:FakeService }
function Get-CimInstance { param([string]$ClassName, [string]$Filter); return $script:FakeServiceInfo }
function Invoke-CimMethod {
    param($InputObject, [string]$MethodName, [hashtable]$Arguments)
    $script:RestoredServiceCommand = $Arguments.PathName
    return [PSCustomObject]@{ ReturnValue = 0 }
}
function Start-Service { param([string]$Name, [string]$ErrorAction); $script:RestoredServiceStarts++ }

# 断言抛出即中止验证，不依赖可能被关闭的 PowerShell 调试断言设置。
function Assert-DeploymentTest([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw "验证失败：$Message" }
}
function Assert-DeploymentThrows([scriptblock]$Action, [string]$Message) {
    $threw = $false
    try { & $Action } catch { $threw = $true }
    Assert-DeploymentTest $threw $Message
}

# 创建完整文件名的最小发布夹具；不把任何实际生产配置或密钥复制到测试目录。
function New-DeploymentFixture([string]$Name) {
    $root = Join-Path $workspace $Name
    $package = Join-Path $root 'package'
    $target = Join-Path $root 'installed'
    [void][IO.Directory]::CreateDirectory($package)
    foreach ($file in @('SwiftExport.WebApi.exe','SwiftExport.WebApi.dll','SwiftExport.WebApi.deps.json','SwiftExport.WebApi.runtimeconfig.json','Microsoft.Extensions.Hosting.WindowsServices.dll')) {
        Set-Content -LiteralPath (Join-Path $package $file) -Value 'new-program' -Encoding UTF8
    }
    Set-Content -LiteralPath (Join-Path $package 'appsettings.Production.json') -Value '{"Marker":"package-must-not-win"}' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $package 'appsettings.Development.json') -Value '{"Marker":"development"}' -Encoding UTF8
    $config = Join-Path $root 'production.json'
    Set-Content -LiteralPath $config -Value '{"Marker":"server","Auth":{"UseDevUser":false}}' -Encoding UTF8
    return @{ Package = $package; Target = $target; Config = $config; Root = $root }
}

$first = New-DeploymentFixture '首次部署'
$result = Invoke-BackendDeployment -PackagePath $first.Package -InstallPath $first.Target -ProductionConfigurationPath $first.Config -Progress { param($m) }
Assert-DeploymentTest ($null -eq $result.BackupPath) '首次部署不应伪造旧版本备份。'
Assert-DeploymentTest ((Get-Content -LiteralPath (Join-Path $first.Target 'appsettings.Production.json') -Raw) -match 'server') '首次部署必须使用指定服务器配置。'
Assert-DeploymentTest (!(Test-Path -LiteralPath (Join-Path $first.Target 'appsettings.Development.json'))) '不能复制开发配置。'
Write-Output '通过：首次部署使用指定配置，并排除发布包配置。'

Set-Content -LiteralPath (Join-Path $first.Target 'obsolete.dll') -Value 'obsolete'
Set-Content -LiteralPath (Join-Path $first.Target 'SwiftExport.WebApi.dll') -Value 'old-program'
[void][IO.Directory]::CreateDirectory((Join-Path $first.Target 'Logs'))
Set-Content -LiteralPath (Join-Path $first.Target 'Logs/old.log') -Value 'old-log'
$result = Invoke-BackendDeployment -PackagePath $first.Package -InstallPath $first.Target -Progress { param($m) }
Assert-DeploymentTest (!(Test-Path -LiteralPath (Join-Path $first.Target 'obsolete.dll'))) '新目录不能残留旧程序集。'
Assert-DeploymentTest ((Get-Content -LiteralPath (Join-Path $first.Target 'appsettings.Production.json') -Raw) -match 'server') '更新不能覆盖服务器配置。'
Assert-DeploymentTest (Test-Path -LiteralPath (Join-Path $result.BackupPath 'obsolete.dll')) '备份须完整保留旧程序。'
Assert-DeploymentTest (Test-Path -LiteralPath (Join-Path $first.Target 'Logs/old.log')) '更新必须保留日志。'
Write-Output '通过：更新保留配置和日志，完整备份旧目录，不残留旧程序集。'

$script:FailFirstReadiness = $true
Set-Content -LiteralPath (Join-Path $first.Target 'SwiftExport.WebApi.dll') -Value 'rollback-program'
$previousStarts = $script:StartCount
Assert-DeploymentThrows { Invoke-BackendDeployment -PackagePath $first.Package -InstallPath $first.Target -Progress { param($m) } } '启动失败必须报告失败。'
Assert-DeploymentTest ((Get-Content -LiteralPath (Join-Path $first.Target 'SwiftExport.WebApi.dll') -Raw).Trim() -eq 'rollback-program') '失败后必须恢复旧程序。'
Assert-DeploymentTest ($script:StartCount -eq $previousStarts + 2) '恢复须重新启动并验证旧实例。'
Assert-DeploymentTest (@(Get-ChildItem -LiteralPath $first.Root -Directory -Filter 'installed_failed_*').Count -eq 1) '失败版本必须保留用于排查。'
Write-Output '通过：新版本启动失败会恢复并启动旧版本，保留失败目录。'

$invalid = New-DeploymentFixture '输入保护'
Assert-DeploymentThrows { Invoke-BackendDeployment -PackagePath $invalid.Package -InstallPath $invalid.Package -ProductionConfigurationPath $invalid.Config } '不能向发布目录自身部署。'
Assert-DeploymentThrows { Invoke-BackendDeployment -PackagePath $invalid.Package -InstallPath (Join-Path $invalid.Package 'nested') -ProductionConfigurationPath $invalid.Config } '不能向发布目录内部部署。'
Assert-DeploymentThrows { Invoke-BackendDeployment -PackagePath $invalid.Package -InstallPath $invalid.Target } '首次部署缺配置必须拒绝。'
Assert-DeploymentThrows { Invoke-BackendDeployment -PackagePath $first.Package -InstallPath $first.Target -ProductionConfigurationPath $first.Config } '更新不能更换生产配置。'
Set-Content -LiteralPath $invalid.Config -Value '{invalid'
Assert-DeploymentThrows { Invoke-BackendDeployment -PackagePath $invalid.Package -InstallPath $invalid.Target -ProductionConfigurationPath $invalid.Config } '配置 JSON 无效必须拒绝。'
Write-Output '通过：路径重叠、缺配置、无效配置与更新换配置被拒绝。'

$lockCase = New-DeploymentFixture '并发保护'
$lock = [IO.File]::Open($lockCase.Target + '.deploy.lock', [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $stops = $script:StopCount
    Assert-DeploymentThrows { Invoke-BackendDeployment -PackagePath $lockCase.Package -InstallPath $lockCase.Target -ProductionConfigurationPath $lockCase.Config } '并发部署必须拒绝。'
    Assert-DeploymentTest ($script:StopCount -eq $stops) '并发拒绝不能停止实例。'
} finally { $lock.Dispose() }
Write-Output '通过：同目标并发部署在停止实例之前被拒绝。'

$empty = New-DeploymentFixture '空目录首次部署'
[void][IO.Directory]::CreateDirectory($empty.Target)
[void](Invoke-BackendDeployment -PackagePath $empty.Package -InstallPath $empty.Target -ProductionConfigurationPath $empty.Config -Progress { param($m) })
Assert-DeploymentTest (Test-Path -LiteralPath (Join-Path $empty.Target 'SwiftExport.WebApi.dll')) '空目录首次部署不得把暂存目录多套一层。'
Write-Output '通过：已有空目录可首次部署，程序文件直接位于安装根目录。'

$storageCase = New-DeploymentFixture '存储保护'
$storagePath = Join-Path $storageCase.Root 'uploaded-files'
[void][IO.Directory]::CreateDirectory($storagePath)
Set-Content -LiteralPath (Join-Path $storagePath 'user.jpg') -Value 'uploaded-file'
@{ FileStorage = @{ RootPath = $storagePath } } | ConvertTo-Json | Set-Content -LiteralPath $storageCase.Config -Encoding UTF8
[void](Invoke-BackendDeployment -PackagePath $storageCase.Package -InstallPath $storageCase.Target -ProductionConfigurationPath $storageCase.Config -Progress { param($m) })
Assert-DeploymentTest ((Get-Content -LiteralPath (Join-Path $storagePath 'user.jpg') -Raw).Trim() -eq 'uploaded-file') '独立上传目录不得被更新。'
@{ FileStorage = @{ Containers = @{ files = @{ RootPath = (Join-Path $storageCase.Target 'Uploads') } } } } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $storageCase.Target 'appsettings.Production.json') -Encoding UTF8
$stops = $script:StopCount
Assert-DeploymentThrows { Invoke-BackendDeployment -PackagePath $storageCase.Package -InstallPath $storageCase.Target -Progress { param($m) } } '程序内的容器存储必须拒绝。'
Assert-DeploymentTest ($script:StopCount -eq $stops) '存储保护检查须在停止实例之前完成。'
Write-Output '通过：独立上传目录保持不变，程序内存储在停止实例前被拒绝。'

$serviceCase = New-DeploymentFixture '已有服务恢复'
[void][IO.Directory]::CreateDirectory($serviceCase.Target)
Copy-Item -LiteralPath $serviceCase.Config -Destination (Join-Path $serviceCase.Target 'appsettings.Production.json')
Set-Content -LiteralPath (Join-Path $serviceCase.Target 'SwiftExport.WebApi.dll') -Value 'old-service-program'
$script:FakeService = [PSCustomObject]@{ Status = 'Running' }
$script:FakeService | Add-Member ScriptMethod WaitForStatus { param($Status, $Timeout) }
$oldCommand = '"' + (Join-Path $serviceCase.Target 'SwiftExport.WebApi.exe') + '" --environment Production --urls "http://127.0.0.1:5999"'
$script:FakeServiceInfo = [PSCustomObject]@{ PathName = $oldCommand }
$script:FailFirstReadiness = $true
Assert-DeploymentThrows { Invoke-BackendDeployment -PackagePath $serviceCase.Package -InstallPath $serviceCase.Target -Mode Service -ListenUrl 'http://127.0.0.1:5080' -Progress { param($m) } } '已有服务更新失败必须报告。'
Assert-DeploymentTest ((Get-Content -LiteralPath (Join-Path $serviceCase.Target 'SwiftExport.WebApi.dll') -Raw).Trim() -eq 'old-service-program') '已有服务须恢复旧程序。'
Assert-DeploymentTest ($script:RestoredServiceCommand -eq $oldCommand) '已有服务须恢复原完整启动参数。'
Assert-DeploymentTest ($script:RestoredServiceStarts -eq 1) '此前运行的已有服务须重新启动。'
Write-Output '通过：已有服务没有工具记录时，仍恢复原程序、完整启动参数和运行状态。'
$script:FakeService = $null
$script:FakeServiceInfo = $null

# 单独调用真实所有权检查，使用查询替身证明不会接管同名其他服务或误停复用的 PID。
$script:OwnedProcessFixture = $null
$script:ServiceFixture = $null
function Get-Process { param([int]$Id, [string]$Name, [string]$ErrorAction); return $script:OwnedProcessFixture }
function Get-CimInstance { param([string]$ClassName, [string]$Filter); return $script:ServiceFixture }
$started = [DateTime]::UtcNow
$ownedState = [PSCustomObject]@{ Mode = 'Process'; ProcessId = 77; StartTicks = $started.Ticks }
$script:OwnedProcessFixture = [PSCustomObject]@{ Id = 77; Path = (Join-Path $first.Target 'SwiftExport.WebApi.exe'); StartTime = $started }
Assert-DeploymentTest ($null -ne (Get-OwnedBackendProcess $ownedState $first.Target)) '合法进程身份应被识别。'
$script:OwnedProcessFixture.StartTime = $started.AddSeconds(1)
Assert-DeploymentThrows { Get-OwnedBackendProcess $ownedState $first.Target } '复用 PID 启动时间不符时必须拒绝。'
$script:OwnedProcessFixture.StartTime = $started
$script:OwnedProcessFixture.Path = 'C:\other\SwiftExport.WebApi.exe'
Assert-DeploymentThrows { Get-OwnedBackendProcess $ownedState $first.Target } '同名其他路径进程必须拒绝。'
$script:ServiceFixture = [PSCustomObject]@{ PathName = '"C:\other\SwiftExport.WebApi.exe" --urls http://127.0.0.1:5080' }
Assert-DeploymentThrows { & $realOwnershipCheck $first.Target 'Service' $null } '不能接管绑定其他目录的同名服务。'
Assert-DeploymentThrows { & $realOwnershipCheck $first.Target 'Service' $ownedState } '不能在更新中切换运行方式。'
Write-Output '通过：PID 重用、其他路径同名进程/服务以及运行方式切换被拒绝。'

# 用独立 loopback TCP 响应验证真实 HTTP 启动探测，不运行业务宿主或连接数据库。
function Get-OwnedBackendProcess($State, [string]$InstallPath) { return [PSCustomObject]@{ Id = 77 } }
$portReservation = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
$portReservation.Start()
$probePort = $portReservation.LocalEndpoint.Port
$portReservation.Stop()
$probeJob = Start-Job -ArgumentList $probePort -ScriptBlock {
    param($Port)
    $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $Port)
    $listener.Start()
    try {
        $connection = $listener.AcceptTcpClient()
        try {
            $stream = $connection.GetStream()
            $buffer = New-Object byte[] 4096
            [void]$stream.Read($buffer, 0, $buffer.Length)
            $body = '{"error":{"code":"UNAUTHENTICATED"}}'
            $response = "HTTP/1.1 401 Unauthorized`r`nX-Correlation-ID: deployment-test`r`nContent-Type: application/json`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n$body"
            $bytes = [Text.Encoding]::ASCII.GetBytes($response)
            $stream.Write($bytes, 0, $bytes.Length)
        } finally { $connection.Dispose() }
    } finally { $listener.Stop() }
}
try { & $realReadinessCheck $first.Target 'Process' $ownedState "http://127.0.0.1:$probePort" }
finally { Stop-Job $probeJob; Remove-Job $probeJob -Force }
function Get-OwnedBackendProcess($State, [string]$InstallPath) { return $null }
Assert-DeploymentThrows { & $realReadinessCheck $first.Target 'Process' $ownedState "http://127.0.0.1:$probePort" } '已退出的新进程不能因旧端口响应而被当作部署成功。'
Write-Output '通过：真实 HTTP 探测识别受认证保护的应用响应，并拒绝已退出进程。'

# 在双击入口使用的 .NET Framework/PowerShell 5.1 下编译无业务替身，验证真实后台进程生命周期。
if ($PSVersionTable.PSVersion.Major -eq 5) {
    $processRoot = Join-Path $workspace '后台进程验证'
    [void][IO.Directory]::CreateDirectory($processRoot)
    $fixtureSource = @'
using System;
using System.IO;
using System.Threading;
/// <summary>只记录测试启动选项并等待终止的隔离进程，不运行业务或数据库代码。</summary>
public static class DeploymentProcessFixture
{
    /// <summary>记录工作目录、生产环境及命令行，供部署工具真实启动测试核对。</summary>
    public static void Main(string[] args)
    {
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-marker.txt"),
            Directory.GetCurrentDirectory() + "\n" + Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            + "\n" + string.Join("|", args));
        Thread.Sleep(60000);
    }
}
'@
    Add-Type -TypeDefinition $fixtureSource -OutputAssembly (Join-Path $processRoot 'SwiftExport.WebApi.exe') -OutputType ConsoleApplication
    function Get-Process {
        param([int]$Id, [string]$Name, [string]$ErrorAction)
        try { return [Diagnostics.Process]::GetProcessById($Id) } catch { return $null }
    }
    function Get-OwnedBackendProcess($State, [string]$InstallPath) { return & $realProcessOwnership $State $InstallPath }
    $running = & $realStartInstance $processRoot 'Process' 'http://127.0.0.1:5080'
    try {
        $marker = Join-Path $processRoot 'startup-marker.txt'
        for ($attempt = 0; $attempt -lt 30 -and !(Test-Path -LiteralPath $marker); $attempt++) { Start-Sleep -Milliseconds 100 }
        $observed = Get-Content -LiteralPath $marker -Raw -Encoding UTF8
        Assert-DeploymentTest ($observed.Contains($processRoot)) '后台进程工作目录必须是安装目录。'
        Assert-DeploymentTest ($observed.Contains('Production')) '后台进程必须显式使用 Production。'
        Assert-DeploymentTest ($observed.Contains('--urls|http://127.0.0.1:5080')) '监听地址必须完整传给后台进程。'
        Assert-DeploymentTest ($null -ne (& $realProcessOwnership $running $processRoot)) '真实进程必须通过 PID、启动时间和路径核验。'
    } finally { & $realStopInstance $processRoot 'Process' $running }
    Assert-DeploymentTest ($null -eq (& $realProcessOwnership $running $processRoot)) '终止后不能仍报告实例存活。'
    Write-Output '通过：真实隔离进程隐藏启动、生产环境/目录/参数、身份核验与句柄终止。'
}
Write-Output "部署隔离验证完成。测试目录保留：$workspace。仅运行无业务的替身进程，未访问数据库、未启动实际后端、未创建或更改系统服务。"
