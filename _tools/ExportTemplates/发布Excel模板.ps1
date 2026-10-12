#requires -Version 7.4
<#
.SYNOPSIS
验证、打包或发布受控 Excel 模板；仅使用 PowerShell/.NET，不依赖 Excel 或 Report。
.DESCRIPTION
先验证完整候选清单和文件，再写入不可变版本文件，最后原子切换 模板目录.json。
发布失败不会替换原清单；已写入但尚未启用的新版本可以保留，旧版本始终保留。
#>
[CmdletBinding()]
param(
    [ValidateSet('Validate', 'Package', 'Publish')][string]$Mode = 'Package',
    [string]$ManifestPath,
    [string]$SourceRoot,
    [string]$OutputRoot,
    [string]$PublishRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$maxTemplateBytes = 20L * 1024 * 1024
$maxExpandedBytes = 100L * 1024 * 1024
$maxManifestBytes = 1024 * 1024
$strictUtf8 = [Text.UTF8Encoding]::new($false, $true)

function Get-AbsoluteRoot {
    <# .SYNOPSIS 将显式根目录解析为绝对目录，拒绝文件路径和驱动器根，防止误发布到整盘。 #>
    param([string]$Path, [string]$ParameterName)
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "必须提供 $ParameterName。" }
    $full = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Path))
    if ($full -eq [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetPathRoot($full))) {
        throw "$ParameterName 不能是驱动器或文件系统根目录。"
    }
    if ([IO.File]::Exists($full)) { throw "$ParameterName 必须是目录。" }
    return $full
}

function Get-Integer {
    <# .SYNOPSIS 只接纳 JSON 整数，拒绝布尔、字符串和浮点数的隐式转换。 #>
    param($Value, [string]$Name, [long]$Maximum)
    if (($Value -isnot [int] -and $Value -isnot [long]) -or $Value -le 0 -or $Value -gt $Maximum) {
        throw "$Name 必须是 1 至 $Maximum 的整数。"
    }
    return [long]$Value
}

function Get-RequiredText {
    <# .SYNOPSIS 验证固定元数据文本，不修剪或修改操作者提供的标识。 #>
    param($Value, [string]$Name, [int]$Maximum)
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value) -or
        $Value -ne $Value.Trim() -or $Value.Length -gt $Maximum) {
        throw "$Name 必须是无首尾空格且最多 $Maximum 字符的非空文本。"
    }
    return $Value
}

function Get-TemplatePath {
    <# .SYNOPSIS 仅从已经验证的标识及版本构造固定模板路径，不接受清单中的任意相对地址。 #>
    param([string]$Root, [string]$Id, [int]$Version)
    return [IO.Path]::Combine($Root, $Id, "v$Version", '模板.xlsx')
}

function Test-XlsxContainer {
    <# .SYNOPSIS 验证标准 XLSX 容器及声明解压上限；拒绝重复或危险部件、宏、坏 ZIP 和 XML 外部实体。 #>
    param([string]$Path)
    $input = [IO.File]::OpenRead($Path)
    try {
        $archive = [IO.Compression.ZipArchive]::new($input, [IO.Compression.ZipArchiveMode]::Read, $true)
        try {
            $expanded = 0L
            $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($entry in $archive.Entries) {
                $segments = $entry.FullName.Split('/')
                if (-not $names.Add($entry.FullName) -or $entry.FullName.Contains('\') -or
                    $entry.FullName.StartsWith('/') -or [IO.Path]::IsPathRooted($entry.FullName) -or
                    $segments -contains '.' -or $segments -contains '..') {
                    throw '模板 XLSX 包含非法或重复部件。'
                }
                if ($entry.Length -gt $maxExpandedBytes - $expanded) { throw '模板声明解压内容超过 100 MiB。' }
                $expanded += $entry.Length
                if ($entry.FullName -match '(?i)(^|/)vbaProject\.bin$') { throw '模板不能包含宏。' }
            }
            $contentTypes = $archive.GetEntry('[Content_Types].xml')
            if ($null -eq $contentTypes -or $null -eq $archive.GetEntry('xl/workbook.xml') -or
                $null -eq $archive.GetEntry('_rels/.rels') -or $null -eq $archive.GetEntry('xl/_rels/workbook.xml.rels')) {
                throw '模板缺少标准 XLSX 工作簿结构。'
            }
            $settings = [Xml.XmlReaderSettings]::new()
            $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
            $settings.XmlResolver = $null
            $settings.MaxCharactersInDocument = $maxExpandedBytes
            $entryStream = $contentTypes.Open()
            try {
                $reader = [Xml.XmlReader]::Create($entryStream, $settings)
                try {
                    $hasWorkbook = $false
                    while ($reader.Read()) {
                        if ($reader.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
                        if ($reader.Depth -eq 0 -and ($reader.LocalName -ne 'Types' -or
                            $reader.NamespaceURI -ne 'http://schemas.openxmlformats.org/package/2006/content-types')) {
                            throw '模板的内容类型根节点无效。'
                        }
                        $contentType = $reader.GetAttribute('ContentType')
                        if ($null -eq $contentType) { continue }
                        if ($contentType -match '(?i)macroEnabled|vbaProject') { throw '模板不能包含宏。' }
                        if ($reader.LocalName -eq 'Override' -and $reader.GetAttribute('PartName') -eq '/xl/workbook.xml' -and
                            $contentType -eq 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml') {
                            $hasWorkbook = $true
                        }
                    }
                    if (-not $hasWorkbook) { throw '仅允许标准 .xlsx 模板。' }
                }
                finally { $reader.Dispose() }
            }
            finally { $entryStream.Dispose() }
            # 所有 XML 部件须完整且不能包含 DTD；这不代替变量、关系引用或 Report 生成验收。
            foreach ($entry in $archive.Entries) {
                if ($entry.FullName -notmatch '(?i)\.(xml|rels)$') { continue }
                $partStream = $entry.Open()
                try {
                    $reader = [Xml.XmlReader]::Create($partStream, $settings)
                    try {
                        while ($reader.Read()) {
                            if ($entry.FullName -eq 'xl/workbook.xml' -and $reader.NodeType -eq [Xml.XmlNodeType]::Element -and
                                $reader.Depth -eq 0 -and ($reader.LocalName -ne 'workbook' -or
                                $reader.NamespaceURI -notin @('http://schemas.openxmlformats.org/spreadsheetml/2006/main',
                                    'http://purl.oclc.org/ooxml/spreadsheetml/main'))) { throw '模板的工作簿根节点无效。' }
                        }
                    }
                    finally { $reader.Dispose() }
                }
                finally { $partStream.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $input.Dispose() }
}

function Get-FileIdentity {
    <# .SYNOPSIS 计算文件真实字节数及 SHA-256，禁止空文件或压缩输入超限。 #>
    param([string]$Path)
    if (-not [IO.File]::Exists($Path)) { throw "模板文件不存在：$Path" }
    $input = [IO.File]::OpenRead($Path)
    try {
        if ($input.Length -le 0 -or $input.Length -gt $maxTemplateBytes) { throw '模板文件必须非空且不超过 20 MiB。' }
        return @{ fileLength = $input.Length; sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($input)).ToLowerInvariant() }
    }
    finally { $input.Dispose() }
}

function Get-ValidatedManifest {
    <# .SYNOPSIS 校验 UTF-8 清单和所有文件；开发源描述可省略长度及哈希，其余字段必须完整。 #>
    param([string]$Path, [string]$Root, [bool]$RequireIdentity)
    if (-not [IO.File]::Exists($Path)) { throw '模板清单不存在。' }
    if ([IO.FileInfo]::new($Path).Length -gt $maxManifestBytes) { throw '模板清单超过 1 MiB。' }
    $bytes = [IO.File]::ReadAllBytes($Path)
    $text = $strictUtf8.GetString($bytes)
    if ($text.StartsWith([char]0xFEFF)) { $text = $text.Substring(1) }
    # ConvertFrom-Json 会覆盖同名 JSON 属性，先按原始对象检查，不能悄悄替发布者选择最后一个值。
    $document = [System.Text.Json.JsonDocument]::Parse($text)
    try {
        $objects = @($document.RootElement)
        if ($document.RootElement.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
            foreach ($property in $document.RootElement.EnumerateObject()) {
                if ($property.Name -ceq 'templates' -and $property.Value.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
                    $objects += @($property.Value.EnumerateArray())
                }
            }
        }
        foreach ($element in $objects) {
            if ($element.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { continue }
            $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($property in $element.EnumerateObject()) {
                if (-not $names.Add($property.Name)) { throw '模板清单不能包含重复 JSON 属性。' }
            }
        }
    }
    finally { $document.Dispose() }
    $manifest = ConvertFrom-Json -InputObject $text -AsHashtable -Depth 32
    if ($manifest -isnot [Collections.IDictionary] -or $manifest.Keys.Count -ne 2 -or
        @($manifest.Keys) -cnotcontains 'formatVersion' -or @($manifest.Keys) -cnotcontains 'templates' -or
        (Get-Integer $manifest.formatVersion 'formatVersion' 1) -ne 1 -or
        $manifest.templates -isnot [array]) {
        throw '清单必须是 formatVersion=1 且 templates 为数组。'
    }
    $allowed = @('templateId', 'displayName', 'documentType', 'dataContractKey', 'dataContractVersion',
        'templateVersion', 'fileLength', 'sha256', 'enabled')
    $required = $allowed | Where-Object { $_ -notin @('fileLength', 'sha256') }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $records = [Collections.Generic.List[object]]::new()
    foreach ($item in $manifest.templates) {
        if ($item -isnot [Collections.IDictionary]) { throw '每条模板记录必须是 JSON 对象。' }
        foreach ($key in $item.Keys) { if ($allowed -cnotcontains $key) { throw "模板记录包含未知字段 $key。" } }
        foreach ($key in $required) { if (-not $item.Contains($key)) { throw "模板记录缺少 $key。" } }
        $id = Get-RequiredText $item.templateId 'templateId' 64
        if ($id -cnotmatch '^[a-z0-9]+(-[a-z0-9]+)*$' -or -not $ids.Add($id)) { throw 'templateId 必须是唯一的小写稳定标识。' }
        $display = Get-RequiredText $item.displayName 'displayName' 200
        $documentType = Get-RequiredText $item.documentType 'documentType' 64
        $contractKey = Get-RequiredText $item.dataContractKey 'dataContractKey' 128
        $contractVersion = Get-Integer $item.dataContractVersion 'dataContractVersion' ([int]::MaxValue)
        $version = Get-Integer $item.templateVersion 'templateVersion' ([int]::MaxValue)
        if ($item.enabled -isnot [bool]) { throw 'enabled 必须明确提供 JSON 布尔值。' }
        if ($RequireIdentity -and (-not $item.Contains('fileLength') -or -not $item.Contains('sha256'))) {
            throw '发布清单必须含有 fileLength 和 sha256，请先打包源描述。'
        }
        if ($item.Contains('fileLength')) {
            $length = Get-Integer $item.fileLength 'fileLength' $maxTemplateBytes
        }
        if ($item.Contains('sha256') -and ($item.sha256 -isnot [string] -or
            $item.sha256 -cnotmatch '^[a-fA-F0-9]{64}$')) { throw '模板 sha256 必须是 64 位十六进制。' }
        # 停用完整元数据时不读取坏文件，确保操作者仍可撤下缺失或损坏的版本。
        if ($item.enabled -or -not ($item.Contains('fileLength') -and $item.Contains('sha256'))) {
            $file = Get-TemplatePath $Root $id $version
            $identity = Get-FileIdentity $file
            if ($item.enabled) { Test-XlsxContainer $file }
            if ($item.Contains('fileLength') -and $length -ne $identity.fileLength) { throw '模板真实长度与清单 fileLength 不一致。' }
            if ($item.Contains('sha256') -and $item.sha256.ToLowerInvariant() -ne $identity.sha256) { throw '模板 SHA-256 与清单不一致。' }
        }
        else {
            $identity = @{ fileLength = $length; sha256 = $item.sha256.ToLowerInvariant() }
        }
        $records.Add([ordered]@{ templateId = $id; displayName = $display; documentType = $documentType;
            dataContractKey = $contractKey; dataContractVersion = $contractVersion; templateVersion = $version;
            fileLength = $identity.fileLength; sha256 = $identity.sha256; enabled = $item.enabled })
    }
    return [ordered]@{ formatVersion = 1; templates = $records.ToArray() }
}

function Assert-SameIdentity {
    <# .SYNOPSIS 已存在的版本只允许相同字节内容，拒绝覆盖同标识同版本的不同文件。 #>
    param([string]$Path, [Collections.IDictionary]$Record)
    $identity = Get-FileIdentity $Path
    if ($identity.fileLength -ne $Record.fileLength -or $identity.sha256 -ne $Record.sha256) {
        throw "版本冲突：$($Record.templateId) v$($Record.templateVersion) 已有不同内容，须建立新版本。"
    }
}

function Write-TemplateDirectory {
    <# .SYNOPSIS 串行发布到显式根目录，先保存完整版本文件，再原子发布完整清单，始终保留旧版本。 #>
    param([string]$Root, [string]$FromRoot, [Collections.IDictionary]$Manifest)
    [IO.Directory]::CreateDirectory($Root) | Out-Null
    $lockPath = [IO.Path]::Combine($Root, '.模板发布.lock')
    $lockStream = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $manifestTemporary = $null
    try {
        # 在任何新版本写入前发现全部同版本冲突，避免明显错误的候选留下多余版本。
        foreach ($record in $Manifest.templates) {
            if (-not $record.enabled) { continue }
            $destination = Get-TemplatePath $Root $record.templateId $record.templateVersion
            if ([IO.File]::Exists($destination)) { Assert-SameIdentity $destination $record }
        }
        foreach ($record in $Manifest.templates) {
            if (-not $record.enabled) { continue }
            $destination = Get-TemplatePath $Root $record.templateId $record.templateVersion
            if ([IO.File]::Exists($destination)) { continue }
            $source = Get-TemplatePath $FromRoot $record.templateId $record.templateVersion
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
            $temporary = "$destination.$([Guid]::NewGuid().ToString('N')).tmp"
            try {
                $input = [IO.File]::OpenRead($source)
                try {
                    if ($input.Length -ne $record.fileLength -or $input.Length -gt $maxTemplateBytes) {
                        throw '复制前模板长度已变化，请重新验证和打包。'
                    }
                    $output = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                    try { $input.CopyTo($output); $output.Flush($true) }
                    finally { $output.Dispose() }
                }
                finally { $input.Dispose() }
                # 复制后再次校验，拒绝验证完成后源文件被其他进程改写的候选。
                Assert-SameIdentity $temporary $record
                Test-XlsxContainer $temporary
                [IO.File]::Move($temporary, $destination, $false)
            }
            finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
        }
        # 新版本文件全部落盘后重新检查最终引用快照，任何启用文件缺失、变更或损坏都保留原清单。
        foreach ($record in $Manifest.templates) {
            if (-not $record.enabled) { continue }
            $destination = Get-TemplatePath $Root $record.templateId $record.templateVersion
            Assert-SameIdentity $destination $record
            Test-XlsxContainer $destination
        }
        $manifestTemporary = [IO.Path]::Combine($Root, ".manifest.$([Guid]::NewGuid().ToString('N')).tmp")
        $json = ConvertTo-Json -InputObject $Manifest -Depth 16
        $manifestBytes = $strictUtf8.GetBytes($json)
        if ($manifestBytes.Length -gt $maxManifestBytes) { throw '输出清单超过 1 MiB。' }
        $output = [IO.File]::Open($manifestTemporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $output.Write($manifestBytes); $output.Flush($true) }
        finally { $output.Dispose() }
        [IO.File]::Move($manifestTemporary, [IO.Path]::Combine($Root, '模板目录.json'), $true)
    }
    finally {
        if ($null -ne $manifestTemporary -and [IO.File]::Exists($manifestTemporary)) { [IO.File]::Delete($manifestTemporary) }
        $lockStream.Dispose()
    }
}

# 默认只打包；服务器根必须通过 Publish 模式或显式 PublishRoot 给出，脚本不读取正式环境配置。
if ($Mode -eq 'Publish' -and [string]::IsNullOrWhiteSpace($ManifestPath)) {
    if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = (Get-Location).Path }
    $ManifestPath = [IO.Path]::Combine([IO.Path]::GetFullPath($OutputRoot), '模板目录.json')
    $SourceRoot = $OutputRoot
}
if ([string]::IsNullOrWhiteSpace($ManifestPath)) { throw '必须提供 ManifestPath。' }
$ManifestPath = [IO.Path]::GetFullPath($ManifestPath)
if ($Mode -eq 'Publish' -and [string]::IsNullOrWhiteSpace($SourceRoot)) { $SourceRoot = $PublishRoot }
$SourceRoot = Get-AbsoluteRoot $SourceRoot 'SourceRoot'
$candidate = Get-ValidatedManifest $ManifestPath $SourceRoot ($Mode -eq 'Publish')
$resultRoot = $SourceRoot
if ($Mode -eq 'Package') {
    $OutputRoot = Get-AbsoluteRoot $OutputRoot 'OutputRoot'
    Write-TemplateDirectory $OutputRoot $SourceRoot $candidate
    $resultRoot = $OutputRoot
}
if ($Mode -eq 'Publish' -or ($Mode -eq 'Package' -and -not [string]::IsNullOrWhiteSpace($PublishRoot))) {
    $PublishRoot = Get-AbsoluteRoot $PublishRoot 'PublishRoot'
    # 打包后从实际包再验证完整身份，不把源描述或先前校验快照当作发布证据。
    $publishSource = if ($Mode -eq 'Package') { $OutputRoot } else { $SourceRoot }
    $publishManifest = if ($Mode -eq 'Package') { [IO.Path]::Combine($OutputRoot, '模板目录.json') } else { $ManifestPath }
    $candidate = Get-ValidatedManifest $publishManifest $publishSource $true
    Write-TemplateDirectory $PublishRoot $publishSource $candidate
    $resultRoot = $PublishRoot
}
[pscustomobject]@{ Mode = $Mode; TemplateCount = $candidate.templates.Count; Root = $resultRoot;
    Manifest = if ($Mode -eq 'Validate') { $ManifestPath } else { [IO.Path]::Combine($resultRoot, '模板目录.json') } }
