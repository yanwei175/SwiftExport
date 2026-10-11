#Requires -Version 5.1
<#! 为两个部署窗口提供管理员启动入口；仅启动窗口，不自动执行安装或更新。 !#>
[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('Caddy首次安装窗口.ps1','后端部署窗口.ps1')][string]$WindowScript)
$ErrorActionPreference = 'Stop'
try {
    $target = Join-Path $PSScriptRoot $WindowScript
    if (!(Test-Path -LiteralPath $target -PathType Leaf)) { throw "缺少窗口脚本：$target" }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    $arguments = '-NoProfile -STA -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -WindowScript "' + $WindowScript + '"'
    # 已提权时直接启动；普通双击通过 RunAs 请求 UAC，不改变系统执行策略。
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        # 在当前提权进程加载窗口，使初始化错误进入下面的统一错误提示。
        & $target
    } else {
        $options = @{ FilePath = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"; ArgumentList = $arguments; WindowStyle = 'Hidden'; Wait = $true; PassThru = $true; Verb = 'RunAs'; ErrorAction = 'Stop' }
        $process = Start-Process @options
        if ($process.ExitCode -ne 0) { throw "管理员窗口进程退出，错误码：$($process.ExitCode)。请查看启动错误提示。" }
    }
} catch {
    # 隐藏控制台时仍展示失败或取消信息，避免窗口闪退而无法判断原因。
    Add-Type -AssemblyName System.Windows.Forms
    [void][Windows.Forms.MessageBox]::Show("窗口未启动：$($_.Exception.Message)", '部署工具', 'OK', 'Error')
    exit 1
}
