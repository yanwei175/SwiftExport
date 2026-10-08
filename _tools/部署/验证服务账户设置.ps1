#Requires -Version 5.1
<#! 使用严格的 CIM/SCM/ACL 替身验证虚拟账户参数，禁止注册实际服务或更改目录权限。 !#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Caddy托管核心.ps1')
$workspace = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) ('artifacts/service-account-tests/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($workspace)
$script:ServiceCreated = $false
$script:Account = ''
$script:ReturnCode = 0
$script:StartCalls = 0
$script:AclCalls = 0
$script:FakeService = [PSCustomObject]@{ Status = 'Stopped' }
$script:FakeService | Add-Member ScriptMethod WaitForStatus { param($Status,$Timeout) }

# 替身按 Windows 虚拟账户契约检查：NULL 才有效，空字符串必须复现原失败。
function Get-Service { param($Name,$ErrorAction); if ($script:ServiceCreated) { return $script:FakeService }; return $null }
function New-Service { param($Name,$DisplayName,$BinaryPathName,$StartupType); $script:ServiceCreated = $true }
function Get-CimInstance { param($ClassName,$Filter); [PSCustomObject]@{ Name = 'fixture' } }
function Invoke-CimMethod {
    param($InputObject,$MethodName,[hashtable]$Arguments)
    if ($Arguments.ContainsKey('StartName')) {
        if (!$Arguments.ContainsKey('StartPassword') -or $null -ne $Arguments['StartPassword']) { return [PSCustomObject]@{ ReturnValue = 22 } }
        $script:Account = $Arguments.StartName
    }
    return [PSCustomObject]@{ ReturnValue = $script:ReturnCode }
}
function icacls.exe { $script:AclCalls++; $global:LASTEXITCODE = 0 }
function Start-Service { param($Name,$ErrorAction); $script:StartCalls++ }
function Assert-ServiceTest([bool]$Condition,[string]$Message) { if (!$Condition) { throw "验证失败：$Message" } }

foreach ($kind in @('Backend','Caddy')) {
    $root = Join-Path $workspace $kind
    $data = Join-Path $workspace ($kind + '-data')
    [void][IO.Directory]::CreateDirectory($root)
    [void][IO.Directory]::CreateDirectory($data)
    [IO.File]::WriteAllText((Join-Path $root 'appsettings.Production.json'),'{}')
    $script:ServiceCreated = $false; $script:Account = ''; $script:StartCalls = 0; $script:AclCalls = 0
    if ($kind -eq 'Backend') { Start-BackendInstance $root 'Service' 'http://127.0.0.1:5081' | Out-Null; $expected = 'NT SERVICE\SwiftExport_WebApi' }
    else { Install-CaddyService $root $data; $expected = 'NT SERVICE\SwiftExport_Caddy' }
    Assert-ServiceTest ($script:Account -eq $expected -and $script:StartCalls -eq 1 -and $script:AclCalls -gt 0) "$kind 必须使用自己的虚拟账户和 NULL 密码后再启动。"
    $script:ServiceCreated = $false; $script:ReturnCode = 22; $script:StartCalls = 0; $script:AclCalls = 0
    $failure = ''
    try {
        if ($kind -eq 'Backend') { Start-BackendInstance $root 'Service' 'http://127.0.0.1:5081' | Out-Null }
        else { Install-CaddyService $root $data }
    } catch { $failure = $_.Exception.Message }
    Assert-ServiceTest ($failure.Contains('22') -and $failure.Contains($expected)) "$kind 失败必须保留返回码和账户。"
    Assert-ServiceTest ($script:StartCalls -eq 0 -and $script:AclCalls -eq 0) "$kind 账户设置失败不能继续授权或启动。"
    $script:ReturnCode = 0
    Write-Output "通过：$kind NULL 密码、独立账户、返回码保留及失败阻断。"
}
Write-Output "服务账户验证完成：$workspace。未操作实际服务或 ACL。"
