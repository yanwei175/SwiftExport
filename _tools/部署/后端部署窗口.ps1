#Requires -Version 5.1
<#! 服务器上的本地部署窗口：读取 Visual Studio 文件夹发布结果，保留配置并调用受限更新流程。 !#>
[CmdletBinding()]
param([switch]$ValidateLayout)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
try { . (Join-Path $PSScriptRoot '后端部署核心.ps1') }
catch { [void][Windows.Forms.MessageBox]::Show($_.Exception.Message, '无法加载部署工具'); exit 1 }

$form = New-Object Windows.Forms.Form
$form.Text = 'SwiftExport 后端部署 / 更新'
$form.ClientSize = New-Object Drawing.Size(700, 550)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.Font = New-Object Drawing.Font('Microsoft YaHei UI', 10)
$script:DeploymentBusy = $false
$settingsPath = Join-Path $PSScriptRoot '部署窗口设置.json'

# 创建窗口中的目录/配置输入行；文件选择器只返回路径，不显示生产配置内容。
function Add-DeploymentInput([string]$Caption, [int]$Top, [bool]$SelectFile) {
    $label = New-Object Windows.Forms.Label
    $label.Text = $Caption
    $label.SetBounds(20, $Top, 660, 24)
    $box = New-Object Windows.Forms.TextBox
    $box.SetBounds(20, ($Top + 26), 560, 28)
    $button = New-Object Windows.Forms.Button
    $button.Text = '选择…'
    $button.SetBounds(590, ($Top + 24), 90, 32)
    $button.Tag = @{ Box = $box; SelectFile = $SelectFile }
    $button.Add_Click({
        $dialog = if ($this.Tag.SelectFile) { New-Object Windows.Forms.OpenFileDialog } else { New-Object Windows.Forms.FolderBrowserDialog }
        try {
            if ($this.Tag.SelectFile) { $dialog.Filter = 'JSON 配置|*.json'; $dialog.Title = '选择服务器生产配置（不选择开发配置）' }
            if ($dialog.ShowDialog($form) -eq 'OK') {
                $this.Tag.Box.Text = if ($this.Tag.SelectFile) { $dialog.FileName } else { $dialog.SelectedPath }
            }
        } finally { $dialog.Dispose() }
    })
    $form.Controls.AddRange(@($label,$box,$button))
    return $box
}

$packageBox = Add-DeploymentInput 'Visual Studio 发布目录（复制到服务器后的完整文件夹）' 15 $false
$targetBox = Add-DeploymentInput '服务器安装目录（更新时保持同一个目录）' 80 $false
$targetBox.Text = 'D:\Apps\SwiftExport_WebApi'
$configurationBox = Add-DeploymentInput '首次部署：选择服务器生产配置 JSON；更新时留空' 145 $true
$modeLabel = New-Object Windows.Forms.Label
$modeLabel.Text = '运行方式'
$modeLabel.SetBounds(20, 214, 95, 24)
$modeBox = New-Object Windows.Forms.ComboBox
$modeBox.DropDownStyle = 'DropDownList'
$modeBox.SetBounds(120, 210, 220, 30)
[void]$modeBox.Items.Add('直接运行（由工具管理进程）')
[void]$modeBox.Items.Add('Windows 服务（开机自动启动）')
$modeBox.SelectedIndex = 0
$urlLabel = New-Object Windows.Forms.Label
$urlLabel.Text = '监听地址（内网访问可用 http://0.0.0.0:5080）'
$urlLabel.SetBounds(20, 251, 660, 24)
$urlBox = New-Object Windows.Forms.TextBox
$urlBox.Text = 'http://127.0.0.1:5080'
$urlBox.SetBounds(20, 279, 660, 28)
$note = New-Object Windows.Forms.Label
$note.Text = '部署会短暂停止后端，请先结束保存/过账操作。更新保留配置；服务模式需管理员运行。'
$note.SetBounds(20, 318, 660, 45)
$deployButton = New-Object Windows.Forms.Button
$deployButton.Text = '部署 / 更新'
$deployButton.SetBounds(20, 365, 160, 40)
$logBox = New-Object Windows.Forms.TextBox
$logBox.Multiline = $true
$logBox.ReadOnly = $true
$logBox.ScrollBars = 'Vertical'
$logBox.SetBounds(20, 420, 660, 110)
$form.Controls.AddRange(@($modeLabel,$modeBox,$urlLabel,$urlBox,$note,$deployButton,$logBox))

# 只记住目录和运行选项；配置文件的内容和密钥不会写进窗口设置。
if (Test-Path -LiteralPath $settingsPath) {
    try {
        $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $packageBox.Text = $settings.PackagePath
        $targetBox.Text = $settings.InstallPath
        $urlBox.Text = $settings.ListenUrl
        $modeBox.SelectedIndex = if ($settings.Mode -eq 'Service') { 1 } else { 0 }
    } catch { $logBox.AppendText("未能读取上次窗口设置，请重新填写。`r`n") }
}
$deployButton.Add_Click({
    if ($script:DeploymentBusy) { return }
    $script:DeploymentBusy = $true
    $deployButton.Enabled = $false
    $form.UseWaitCursor = $true
    try {
        $options = @{
            PackagePath = $packageBox.Text.Trim()
            InstallPath = $targetBox.Text.Trim()
            Mode = $(if ($modeBox.SelectedIndex -eq 1) { 'Service' } else { 'Process' })
            ListenUrl = $urlBox.Text.Trim()
        }
        # 工具放在独立目录，避免更新时移动正在使用的脚本和窗口设置。
        $toolPath = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
        $installPath = [IO.Path]::GetFullPath($options.InstallPath).TrimEnd('\')
        if ($toolPath -eq $installPath -or $toolPath.StartsWith($installPath + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '请把部署工具放在安装目录之外。' }
        $options | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding UTF8
        $options.ProductionConfigurationPath = $configurationBox.Text.Trim()
        $options.Progress = {
            param($message)
            $logBox.AppendText((Get-Date -Format 'HH:mm:ss') + ' ' + $message + "`r`n")
            [Windows.Forms.Application]::DoEvents()
        }
        $result = Invoke-BackendDeployment @options
        $configurationBox.Clear()
        if ($result.BackupPath) { $logBox.AppendText('旧版本备份：' + $result.BackupPath + "`r`n") }
        [void][Windows.Forms.MessageBox]::Show($form, '部署成功。请再验证客户端登录和本次功能。', '完成')
    } catch {
        $logBox.AppendText($_.Exception.Message + "`r`n")
        [void][Windows.Forms.MessageBox]::Show($form, $_.Exception.Message, '部署未完成', 'OK', 'Error')
    } finally {
        $script:DeploymentBusy = $false
        $deployButton.Enabled = $true
        $form.UseWaitCursor = $false
    }
})
$form.Add_FormClosing({ if ($script:DeploymentBusy) { $_.Cancel = $true } })
try {
    if ($ValidateLayout) { Write-Output "部署窗口构建成功：$($form.Controls.Count) 个控件，未执行部署。" }
    else { [void]$form.ShowDialog() }
} finally { $form.Dispose() }
