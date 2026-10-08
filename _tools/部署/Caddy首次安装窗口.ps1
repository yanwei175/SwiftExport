#Requires -Version 5.1
<#! 首次安装的可视入口；目录使用明确默认值，进阶参数可通过同目录首次安装脚本传入。 !#>
[CmdletBinding()]
param([switch]$ValidateLayout)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$form = New-Object Windows.Forms.Form
$form.Text = 'SwiftExport 首次安装 HTTPS / 从测试 IIS 切换'
$form.ClientSize = New-Object Drawing.Size(720,690)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.Font = New-Object Drawing.Font('Microsoft YaHei UI',10)
$script:InstallingHosting = $false

# 只选择路径，不显示生产配置或其密钥；文件内容由预检验证。
function Add-HostingInput([string]$Caption,[int]$Top,[bool]$File) {
    $label = New-Object Windows.Forms.Label
    $label.Text = $Caption
    $label.SetBounds(20,$Top,680,24)
    $box = New-Object Windows.Forms.TextBox
    $box.SetBounds(20,($Top + 26),570,28)
    $button = New-Object Windows.Forms.Button
    $button.Text = '选择…'
    $button.SetBounds(605,($Top + 24),90,32)
    $button.Tag = @{ Box = $box; File = $File }
    $button.Add_Click({
        $dialog = if ($this.Tag.File) { New-Object Windows.Forms.OpenFileDialog } else { New-Object Windows.Forms.FolderBrowserDialog }
        try {
            if ($this.Tag.File) { $dialog.Filter = '服务器生产 JSON|*.json' }
            if ($dialog.ShowDialog($form) -eq 'OK') { $this.Tag.Box.Text = if ($this.Tag.File) { $dialog.FileName } else { $dialog.SelectedPath } }
        } finally { $dialog.Dispose() }
    })
    $form.Controls.AddRange(@($label,$box,$button))
    return $box
}
$packageBox = Add-HostingInput '已复制到服务器的 Windows x64 发布目录' 15 $false
$configBox = Add-HostingInput '完整生产配置（保留现有 PG、JWT、图片目录配置）' 85 $true
$ipLabel = New-Object Windows.Forms.Label
$ipLabel.Text = '服务器 IPv4 地址'
$ipLabel.SetBounds(20,160,200,24)
$ipBox = New-Object Windows.Forms.TextBox
$ipBox.Text = '192.168.3.12'
$ipBox.SetBounds(220,157,260,28)
$iisLabel = New-Object Windows.Forms.Label
$iisLabel.Text = '要停止的测试 IIS 站点'
$iisLabel.SetBounds(20,203,200,24)
$iisBox = New-Object Windows.Forms.TextBox
$iisBox.Text = 'SwiftExport_WebApi'
$iisBox.SetBounds(220,200,260,28)
$noIisBox = New-Object Windows.Forms.CheckBox
$noIisBox.Text = '不迁移 IIS（已清理/新装）'
$noIisBox.SetBounds(490,200,210,28)
$noIisBox.Add_CheckedChanged({ $iisBox.Enabled = !$noIisBox.Checked })
$paths = New-Object Windows.Forms.Label
$paths.Text = "后端：D:\Apps\SwiftExport_Service（127.0.0.1:5081）`r`nCaddy：D:\Apps\SwiftExport_Caddy（HTTPS 443）`r`n持久 CA：D:\ERPDeploy\CaddyData`r`n客户端证书：D:\ERPDeploy\客户端证书"
$paths.SetBounds(20,246,680,105)
$note = New-Object Windows.Forms.Label
$note.Text = '请以管理员身份运行。安装前须完成当前后端发布。只停止指定测试站点，保留旧 IIS 配置和文件。上述目标须为空；其他目录可通过脚本参数指定。'
$note.SetBounds(20,358,680,72)
$inspect = New-Object Windows.Forms.Button
$inspect.Text = '仅检查配置'
$inspect.SetBounds(20,436,150,38)
$install = New-Object Windows.Forms.Button
$install.Text = '首次安装 / 切换'
$install.SetBounds(190,436,190,38)
$log = New-Object Windows.Forms.TextBox
$log.Multiline = $true
$log.ReadOnly = $true
$log.ScrollBars = 'Vertical'
$log.SetBounds(20,495,680,175)
$form.Controls.AddRange(@($ipLabel,$ipBox,$iisLabel,$iisBox,$noIisBox,$paths,$note,$inspect,$install,$log))

# 首次安装成功后记住新后端的服务目录和回环地址，让日常更新窗口使用正确目标。
function Invoke-HostingWindow([bool]$OnlyCheck) {
    if ($script:InstallingHosting) { return }
    $script:InstallingHosting = $true
    $inspect.Enabled = $false
    $install.Enabled = $false
    $form.UseWaitCursor = $true
    try {
        $options = @{
            PackagePath = $packageBox.Text.Trim(); ProductionConfigurationPath = $configBox.Text.Trim()
            ServerAddress = $ipBox.Text.Trim(); IisSiteName = $iisBox.Text.Trim()
            NoIis = $noIisBox.Checked; PlanOnly = $OnlyCheck
            Progress = { param($message) $log.AppendText($message + "`r`n"); [Windows.Forms.Application]::DoEvents() }
        }
        $result = & (Join-Path $PSScriptRoot '首次安装Caddy托管.ps1') @options
        if ($OnlyCheck) { $log.AppendText("预检通过；尚未安装或停止任何服务。`r`n"); return }
        @{ PackagePath = $options.PackagePath; InstallPath = $result.BackendInstallPath; Mode = 'Service'; ListenUrl = $result.ListenUrl } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot '部署窗口设置.json') -Encoding UTF8
        [void][Windows.Forms.MessageBox]::Show($form, '安装通过。请将客户端证书目录复制到测试电脑，以管理员身份运行证书安装入口，再验收登录、图片读取及上传。日常更新使用后端部署窗口。', '首次安装完成')
    } catch {
        $log.AppendText($_.Exception.Message + "`r`n")
        [void][Windows.Forms.MessageBox]::Show($form,$_.Exception.Message,'安装未完成','OK','Error')
    } finally {
        $script:InstallingHosting = $false
        $inspect.Enabled = $true
        $install.Enabled = $true
        $form.UseWaitCursor = $false
    }
}
$inspect.Add_Click({ Invoke-HostingWindow $true })
$install.Add_Click({ Invoke-HostingWindow $false })
$form.Add_FormClosing({ if ($script:InstallingHosting) { $_.Cancel = $true } })
try {
    if ($ValidateLayout) { Write-Output "Caddy 窗口构建成功：$($form.Controls.Count) 个控件，未执行安装。" }
    else { [void]$form.ShowDialog() }
} finally { $form.Dispose() }
