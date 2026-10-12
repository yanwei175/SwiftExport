#requires -Version 7.4
<# .SYNOPSIS 在唯一临时工作区验证清单、打包、不可变版本和原子发布，不触及真实服务器。 #>
[CmdletBinding()]
param([switch]$KeepArtifacts)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$publisher = Join-Path $PSScriptRoot '发布Excel模板.ps1'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifactRoot = Join-Path $repoRoot '.artifacts/export-templates-publish-agent'
$scratch = Join-Path $artifactRoot ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$passed = 0

function Assert-True {
    <# .SYNOPSIS 断言可观察文件结果，失败时直接报告验收场景。 #>
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "验收失败：$Message" }
}

function Assert-Fails {
    <# .SYNOPSIS 确认无效候选被拒绝，不能把成功或其它未运行结果算作验收通过。 #>
    param([scriptblock]$Action, [string]$Message)
    $failed = $false
    try { & $Action | Out-Null }
    catch { $failed = $true }
    Assert-True $failed $Message
}

function Write-Json {
    <# .SYNOPSIS 保存当前场景的独立 UTF-8 候选清单。 #>
    param([string]$Path, $Value)
    [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 16), $utf8)
}

function New-SourceManifest {
    <# .SYNOPSIS 使用不含业务含义的演示契约建立源描述，长度及哈希由发布脚本计算。 #>
    param([int]$Version = 1)
    return [ordered]@{ formatVersion = 1; templates = @([ordered]@{
        templateId = 'demo-order'; displayName = '测试单'; documentType = 'TEST'; dataContractKey = 'demo-order';
        dataContractVersion = 1; templateVersion = $Version; enabled = $true }) }
}

function New-Xlsx {
    <# .SYNOPSIS 在临时目录创建基本 OOXML 文件用于发布验证；不冒充正式业务模板。 #>
    param([string]$Path, [string]$Marker = 'first', [switch]$Macro, [switch]$ExpandedTooLarge)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    $stream = [IO.File]::Create($Path)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $mainType = if ($Macro) { 'application/vnd.ms-excel.sheet.macroEnabled.main+xml' }
                else { 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml' }
            $parts = [ordered]@{
                '[Content_Types].xml' = "<Types xmlns=`"http://schemas.openxmlformats.org/package/2006/content-types`"><Default Extension=`"rels`" ContentType=`"application/vnd.openxmlformats-package.relationships+xml`"/><Default Extension=`"xml`" ContentType=`"application/xml`"/><Override PartName=`"/xl/workbook.xml`" ContentType=`"$mainType`"/><Override PartName=`"/xl/worksheets/sheet1.xml`" ContentType=`"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml`"/></Types>"
                '_rels/.rels' = '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>'
                'xl/_rels/workbook.xml.rels' = '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>'
                'xl/workbook.xml' = '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>'
                'xl/worksheets/sheet1.xml' = "<worksheet xmlns=`"http://schemas.openxmlformats.org/spreadsheetml/2006/main`"><sheetData><row r=`"1`"><c r=`"A1`" t=`"inlineStr`"><is><t>$Marker</t></is></c></row></sheetData></worksheet>"
            }
            foreach ($part in $parts.GetEnumerator()) {
                $entry = $archive.CreateEntry($part.Key)
                $output = $entry.Open()
                try { $output.Write($utf8.GetBytes($part.Value)) }
                finally { $output.Dispose() }
            }
            if ($ExpandedTooLarge) {
                $output = $archive.CreateEntry('xl/large.bin').Open()
                try {
                    $block = [byte[]]::new(1024 * 1024)
                    for ($index = 0; $index -lt 101; $index++) { $output.Write($block) }
                }
                finally { $output.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
}

try {
    $source = Join-Path $scratch 'source'
    $package = Join-Path $scratch 'package'
    $server = Join-Path $scratch 'server'
    $manifestPath = Join-Path $scratch '源描述.json'
    $template = Join-Path $source 'demo-order/v1/模板.xlsx'
    New-Xlsx $template
    Write-Json $manifestPath (New-SourceManifest)

    & $publisher -Mode Validate -ManifestPath $manifestPath -SourceRoot $source | Out-Null
    Assert-True (-not [IO.Directory]::Exists($package)) 'Validate 不能创建包。'
    & $publisher -Mode Package -ManifestPath $manifestPath -SourceRoot $source -OutputRoot $package | Out-Null
    $packedManifest = Join-Path $package '模板目录.json'
    $packed = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($packedManifest)) -AsHashtable
    Assert-True ($packed.templates.Count -eq 1 -and $packed.templates[0].fileLength -gt 0 -and $packed.templates[0].sha256.Length -eq 64) '包须补齐真实长度及哈希。'
    Assert-True (([IO.File]::ReadAllBytes($packedManifest))[0] -ne 0xEF) '输出清单使用 UTF-8 无 BOM。'
    $passed++

    $duplicatePropertyPath = Join-Path $scratch '重复属性.json'
    [IO.File]::WriteAllText($duplicatePropertyPath, '{"formatVersion":2,"formatVersion":1,"templates":[]}', $utf8)
    Assert-Fails { & $publisher -Mode Validate -ManifestPath $duplicatePropertyPath -SourceRoot $source } '拒绝重复 JSON 属性，不选择最后一个值。'
    $passed++

    $bomManifest = Join-Path $scratch 'BOM源描述.json'
    $bomBytes = [Text.Encoding]::UTF8.GetPreamble() + $utf8.GetBytes([IO.File]::ReadAllText($manifestPath))
    [IO.File]::WriteAllBytes($bomManifest, $bomBytes)
    & $publisher -Mode Validate -ManifestPath $bomManifest -SourceRoot $source | Out-Null
    $passed++

    & $publisher -Mode Publish -OutputRoot $package -PublishRoot $server | Out-Null
    $serverManifest = Join-Path $server '模板目录.json'
    Assert-True ([IO.File]::Exists((Join-Path $server 'demo-order/v1/模板.xlsx'))) '发布须保存版本文件。'
    $initialManifest = [IO.File]::ReadAllText($serverManifest)
    & $publisher -Mode Publish -OutputRoot $package -PublishRoot $server | Out-Null
    Assert-True ([IO.File]::ReadAllText($serverManifest) -eq $initialManifest) '同内容重复发布应幂等。'
    $passed++

    foreach ($kind in @('DuplicateId', 'MissingEnabled', 'InvalidEnabled', 'InvalidId', 'InvalidVersion', 'WrongLength', 'WrongHash', 'WrongFormat', 'MissingFile')) {
        $candidate = New-SourceManifest
        switch ($kind) {
            'DuplicateId' { $candidate.templates += (New-SourceManifest).templates[0] }
            'MissingEnabled' { $candidate.templates[0].Remove('enabled') }
            'InvalidEnabled' { $candidate.templates[0].enabled = 'true' }
            'InvalidId' { $candidate.templates[0].templateId = '../escape' }
            'InvalidVersion' { $candidate.templates[0].templateVersion = 1.5 }
            'WrongLength' { $candidate.templates[0].fileLength = 1 }
            'WrongHash' { $candidate.templates[0].sha256 = 'a' * 64 }
            'WrongFormat' { $candidate.formatVersion = 2 }
            'MissingFile' { $candidate.templates[0].templateVersion = 99 }
        }
        $invalidPath = Join-Path $scratch "$kind.json"
        Write-Json $invalidPath $candidate
        Assert-Fails { & $publisher -Mode Package -ManifestPath $invalidPath -SourceRoot $source -OutputRoot $package } $kind
        Assert-True ([IO.File]::ReadAllText($packedManifest) -eq $initialManifest) "$kind 失败必须保留完整包清单。"
        $passed++
    }

    $badEncoding = Join-Path $scratch '坏编码.json'
    [IO.File]::WriteAllBytes($badEncoding, [byte[]]@(0xFF, 0xFE, 0x7B, 0))
    Assert-Fails { & $publisher -Mode Validate -ManifestPath $badEncoding -SourceRoot $source } '拒绝 UTF-16/坏字节而不自动猜编码。'
    $passed++

    New-Xlsx $template -Marker 'changed'
    Write-Json $manifestPath (New-SourceManifest)
    Assert-Fails { & $publisher -Mode Package -ManifestPath $manifestPath -SourceRoot $source -OutputRoot $package } '同 ID 同版本不同内容应拒绝。'
    Assert-True ([IO.File]::ReadAllText($packedManifest) -eq $initialManifest) '版本冲突不切换旧清单。'
    $passed++

    $conflictPackage = Join-Path $scratch 'conflict-package'
    & $publisher -Mode Package -ManifestPath $manifestPath -SourceRoot $source -OutputRoot $conflictPackage | Out-Null
    Assert-Fails { & $publisher -Mode Publish -OutputRoot $conflictPackage -PublishRoot $server } '服务器拒绝同版本不同字节。'
    Assert-True ([IO.File]::ReadAllText($serverManifest) -eq $initialManifest) '服务器版本冲突不切换旧清单。'
    $passed++

    foreach ($kind in @('Macro', 'CorruptZip', 'CompressedTooLarge', 'ExpandedTooLarge')) {
        $badRoot = Join-Path $scratch $kind
        $badTemplate = Join-Path $badRoot 'demo-order/v1/模板.xlsx'
        New-Xlsx $badTemplate -Macro:($kind -eq 'Macro') -ExpandedTooLarge:($kind -eq 'ExpandedTooLarge')
        if ($kind -eq 'CorruptZip') { [IO.File]::WriteAllText($badTemplate, 'not-xlsx') }
        if ($kind -eq 'CompressedTooLarge') {
            $large = [IO.File]::OpenWrite($badTemplate)
            try { $large.SetLength(20L * 1024 * 1024 + 1) }
            finally { $large.Dispose() }
        }
        Assert-Fails { & $publisher -Mode Validate -ManifestPath $manifestPath -SourceRoot $badRoot } $kind
        $passed++
    }

    foreach ($entryName in @('xl/workbook.xml', 'xl\evil.bin', '/absolute.bin', 'C:/absolute.bin', 'xl/./evil.bin', 'xl/../evil.bin')) {
        $badEntryRoot = Join-Path $scratch "BadEntry-$passed"
        $badEntryTemplate = Join-Path $badEntryRoot 'demo-order/v1/模板.xlsx'
        New-Xlsx $badEntryTemplate
        $badEntryStream = [IO.File]::Open($badEntryTemplate, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite)
        try {
            $archive = [IO.Compression.ZipArchive]::new($badEntryStream, [IO.Compression.ZipArchiveMode]::Update, $true)
            try {
                # CreateEntry 保留已有同名部件，构造真实重复 ZIP 条目而非仅重复清单字段。
                $output = $archive.CreateEntry($entryName).Open()
                try { $output.Write($utf8.GetBytes('invalid-part')) }
                finally { $output.Dispose() }
            }
            finally { $archive.Dispose() }
        }
        finally { $badEntryStream.Dispose() }
        Assert-Fails { & $publisher -Mode Validate -ManifestPath $manifestPath -SourceRoot $badEntryRoot } "拒绝重复或危险 ZIP 部件：$entryName"
        $passed++
    }

    $badXmlRoot = Join-Path $scratch 'BadXml'
    $badXmlTemplate = Join-Path $badXmlRoot 'demo-order/v1/模板.xlsx'
    New-Xlsx $badXmlTemplate
    $badXmlStream = [IO.File]::Open($badXmlTemplate, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite)
    try {
        $archive = [IO.Compression.ZipArchive]::new($badXmlStream, [IO.Compression.ZipArchiveMode]::Update, $true)
        try {
            $output = $archive.GetEntry('xl/worksheets/sheet1.xml').Open()
            try { $output.SetLength(0); $output.Write($utf8.GetBytes('<worksheet>')) }
            finally { $output.Dispose() }
        }
        finally { $archive.Dispose() }
    }
    finally { $badXmlStream.Dispose() }
    Assert-Fails { & $publisher -Mode Validate -ManifestPath $manifestPath -SourceRoot $badXmlRoot } '拒绝工作表 XML 损坏。'
    $passed++

    New-Xlsx (Join-Path $source 'demo-order/v2/模板.xlsx') -Marker 'second'
    Write-Json $manifestPath (New-SourceManifest 2)
    & $publisher -Mode Package -ManifestPath $manifestPath -SourceRoot $source -OutputRoot $package | Out-Null
    & $publisher -Mode Publish -OutputRoot $package -PublishRoot $server | Out-Null
    Assert-True ([IO.File]::Exists((Join-Path $server 'demo-order/v1/模板.xlsx'))) '升级后保留旧版。'
    $active = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($serverManifest)) -AsHashtable
    Assert-True ($active.templates[0].templateVersion -eq 2) '清单最后切换到新版。'
    $passed++

    $active.templates[0].enabled = $false
    $disabledPath = Join-Path $scratch '停用.json'
    Write-Json $disabledPath $active
    & $publisher -Mode Publish -ManifestPath $disabledPath -PublishRoot $server | Out-Null
    Assert-True (-not (ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($serverManifest))).templates[0].enabled) '完整清单停用应生效。'
    $serverV2 = Join-Path $server 'demo-order/v2/模板.xlsx'
    $validV2Bytes = [IO.File]::ReadAllBytes($serverV2)
    [IO.File]::Delete($serverV2)
    & $publisher -Mode Publish -ManifestPath $disabledPath -PublishRoot $server | Out-Null
    Assert-True (-not [IO.File]::Exists($serverV2)) '停用缺失文件不要求重新复制。'
    $passed++
    [IO.File]::WriteAllText($serverV2, 'corrupt-disabled-file')
    & $publisher -Mode Publish -ManifestPath $disabledPath -PublishRoot $server | Out-Null
    Assert-True ([IO.File]::ReadAllText($serverV2) -eq 'corrupt-disabled-file') '停用坏文件不覆盖或删除。'
    $passed++
    $active.templates[0].enabled = $true
    $reenablePath = Join-Path $scratch '重新启用.json'
    Write-Json $reenablePath $active
    Assert-Fails { & $publisher -Mode Publish -ManifestPath $reenablePath -PublishRoot $server } '重新启用坏文件必须拒绝。'
    Assert-True (-not (ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($serverManifest))).templates[0].enabled) '拒绝重新启用后仍停用。'
    $passed++
    [IO.File]::WriteAllBytes($serverV2, $validV2Bytes)
    $rollbackPath = Join-Path $scratch '回退.json'
    [IO.File]::WriteAllText($rollbackPath, $initialManifest, $utf8)
    & $publisher -Mode Publish -ManifestPath $rollbackPath -PublishRoot $server | Out-Null
    Assert-True ((ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($serverManifest))).templates[0].templateVersion -eq 1) '重新指向已保留版本。'
    $passed++

    $heldLock = [IO.File]::Open((Join-Path $server '.模板发布.lock'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try { Assert-Fails { & $publisher -Mode Publish -OutputRoot $package -PublishRoot $server } '同根并发发布须被独占锁阻止。' }
    finally { $heldLock.Dispose() }
    Assert-True ([IO.File]::ReadAllText($serverManifest) -eq $initialManifest) '锁冲突保留原清单。'
    Assert-True (@([IO.Directory]::EnumerateFiles($server, '*.tmp', [IO.SearchOption]::AllDirectories)).Count -eq 0) '失败路径不留下临时文件。'
    $passed++

    # Windows 上持有无 Delete 共享的旧清单，验证最后一步替换失败仍保留旧清单。
    if ($IsWindows) {
        New-Xlsx (Join-Path $source 'demo-order/v3/模板.xlsx') -Marker 'third'
        Write-Json $manifestPath (New-SourceManifest 3)
        & $publisher -Mode Package -ManifestPath $manifestPath -SourceRoot $source -OutputRoot $package | Out-Null
        $heldManifest = [IO.File]::Open($serverManifest, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try { Assert-Fails { & $publisher -Mode Publish -OutputRoot $package -PublishRoot $server } '清单原子切换失败须报告失败。' }
        finally { $heldManifest.Dispose() }
        Assert-True ([IO.File]::ReadAllText($serverManifest) -eq $initialManifest) '最后发布失败不损坏旧清单。'
        Assert-True ([IO.File]::Exists((Join-Path $server 'demo-order/v3/模板.xlsx'))) '清单失败前新增文件保持完整，尚未启用。'
        Assert-True (@([IO.Directory]::EnumerateFiles($server, '*.tmp', [IO.SearchOption]::AllDirectories)).Count -eq 0) '清单切换失败清理临时文件。'
        $passed++
    }
    [pscustomobject]@{ PassedScenarios = $passed; ScratchRoot = $scratch; KeptArtifacts = [bool]$KeepArtifacts }
}
finally {
    if (-not $KeepArtifacts -and [IO.Directory]::Exists($scratch)) {
        # 删除前核对绝对路径仍位于本脚本专用验收目录，不跨 Shell 构造删除命令。
        $resolvedScratch = [IO.Path]::GetFullPath($scratch)
        $safePrefix = [IO.Path]::GetFullPath($artifactRoot) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolvedScratch.StartsWith($safePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '拒绝清理预期目录之外的路径。' }
        Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
    }
}
