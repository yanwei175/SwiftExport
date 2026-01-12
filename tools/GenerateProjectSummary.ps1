<#
.SYNOPSIS
  为指定解决方案项目生成 AI 可读的 PROJECT_SUMMARY.md（解决方案/项目文件树 + 类型/属性/方法/DTO 映射线索）

.USAGE
  在仓库根目录运行：
    powershell -ExecutionPolicy Bypass -File .\tools\GenerateProjectSummary.ps1
  或指定项目清单：
    .\tools\GenerateProjectSummary.ps1 -IncludeProjects SwiftExport.Core,SwiftExport.UiWinForm

.NOTES
  - 默认排除 bin/, obj/, packages/, .git, 和 *.Designer.cs
  - 优先包含参数中列出的项目目录（若找到），否则扫描整个仓库。
#>

param(
    [string]$RootPath = (Get-Location).Path,
    [string]$OutFile = "PROJECT_SUMMARY.md",
    [string[]]$IncludeProjects = @("SwiftExport.AppLayer","SwiftExport.Core","SwiftExport.Infrastructure","SwiftExport.UI.Framework","SwiftExport.UiWinForm","SwiftExport.Utility","SwiftExportAddIn"),
    [string[]]$ExcludeDirs = @("bin","obj","packages",".git"),
    [string[]]$ExcludeFilePatterns = @("*.Designer.cs")
)

Write-Host "Scanning root: $RootPath"

# 允许的扩展 - 包含解决方案/项目/源码/资源/文档
$allowedExt = @(".sln", ".csproj", ".cs", ".resx", ".config", ".xaml", ".json", ".md")

# 解析 RootPath 为绝对路径
$rootFull = (Resolve-Path $RootPath).ProviderPath.TrimEnd('\','/')

# 尝试定位指定项目目录或 .csproj
$projectDirs = @()
foreach ($proj in $IncludeProjects) {
    # 1. 找到与项目名同名的文件夹（不区分大小写）
    $dirs = Get-ChildItem -Path $rootFull -Recurse -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -ieq $proj }
    foreach ($d in $dirs) { $projectDirs += $d.FullName }

    # 2. 如果没有找到，再按 csproj 文件名匹配（例如 SwiftExport.Core.csproj）
    if (-not ($projectDirs | Where-Object { $_ -match ("[\\\/]" + [regex]::Escape($proj) + "$") })) {
        $csprojFiles = Get-ChildItem -Path $rootFull -Recurse -File -Filter "$proj*.csproj" -ErrorAction SilentlyContinue
        foreach ($f in $csprojFiles) { $projectDirs += (Split-Path $f.FullName -Parent) }
    }
}

# 去重并保持存在性
$projectDirs = $projectDirs | Select-Object -Unique | Where-Object { Test-Path $_ }

# 收集文件集合：优先项目目录，否则整个仓库
$collectedFiles = @()
if ($projectDirs.Count -gt 0) {
    Write-Host "Found project directories: $($projectDirs -join '; ')"
    foreach ($dir in $projectDirs) {
        $files = Get-ChildItem -Path $dir -Recurse -File -ErrorAction SilentlyContinue | Where-Object {
                if (-not ($allowedExt -contains $_.Extension.ToLower())) { return $false }
                foreach ($d in $ExcludeDirs) { if ($_.FullName -match [regex]::Escape([System.IO.Path]::Combine($dir, $d))) { return $false } }
                foreach ($p in $ExcludeFilePatterns) { if ($_.Name -like $p) { return $false } }
                return $true
            }
        $collectedFiles += $files
    }
}

# 如果没有找到任何项目目录或收集为空，则回退到扫描整个仓库
if ($collectedFiles.Count -eq 0) {
    Write-Host "No specified project directories found or no files collected. Scanning entire repository..."
    $collectedFiles = Get-ChildItem -Path $rootFull -Recurse -File -ErrorAction SilentlyContinue | Where-Object {
            if (-not ($allowedExt -contains $_.Extension.ToLower())) { return $false }
            foreach ($d in $ExcludeDirs) { if ($_.FullName -match [regex]::Escape([System.IO.Path]::Combine($rootFull, $d))) { return $false } }
            foreach ($p in $ExcludeFilePatterns) { if ($_.Name -like $p) { return $false } }
            return $true
        }
}

$allFiles = $collectedFiles | Sort-Object FullName

if ($allFiles.Count -eq 0) {
    Write-Host "No files found to process. Exiting."
    exit 1
}

# 生成文件树文本（基于相对路径）
function Build-Tree {
    param($files, $root)
    $rootFull = (Resolve-Path $root).ProviderPath.TrimEnd('\','/')
    $relPaths = $files | ForEach-Object {
        $p = (Resolve-Path $_.FullName).ProviderPath
        if ($p.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) { $p.Substring($rootFull.Length).TrimStart('\','/') } else { $_.FullName }
    } | Sort-Object

    $treeLines = @()
    foreach ($rel in $relPaths) {
        $parts = $rel -split '[\\/]'
        $parts = $parts | Where-Object { $_ -ne "" }
        $line = ""
        for ($i=0; $i -lt $parts.Length; $i++) { $prefix = ('  ' * $i) + "|- "; $line = $prefix + $parts[$i] }
        $treeLines += $line
    }
    return $treeLines
}

$tree = Build-Tree -files $allFiles -root $rootFull

# 轻量级 C# 类型解析（仅针对 .cs 文件）
$typeHeaderRegex = '^\s*(public|internal|private|protected)?\s*(static\s+)?(partial\s+)?(class|interface|struct|enum)\s+([A-Za-z0-9_`]+(?:<[^>]+>)?)'
$namespaceRegex = '^\s*namespace\s+([A-Za-z0-9_.]+)'
$methodSigRegex = '^\s*(public|private|protected|internal)?\s*(static\s+)?([^\s]+\s+)?([A-Za-z0-9_<>]+)\s*\(([^\)]*)\)\s*(\{?|;)$'
$propertyRegex = '^\s*(public|private|protected|internal)\s+(static\s+)?([A-Za-z0-9_<>,\.\[\]\? ]+)\s+([A-Za-z0-9_]+)\s*\{\s*(get;|set;|get; set;|set; get;|get\s*\{|\s*)'

$types = @()
$csFiles = $allFiles | Where-Object { $_.Extension -ieq ".cs" }

foreach ($file in $csFiles) {
    $text = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
    if (-not $text) { continue }
    $lines = $text -split "`r?`n"
    $currentNamespace = ""
    for ($i=0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ($line -match $namespaceRegex) { $currentNamespace = $Matches[1] }
        if ($line -match $typeHeaderRegex) {
            $typeKind = $Matches[4]
            $typeName = $Matches[5]
            # 收集类型体（简单花括号计数）
            $startIndex = $i
            $braceCount = 0
            $foundStart = $false
            $endIndex = $null
            for ($j=$i; $j -lt $lines.Length; $j++) {
                $l = $lines[$j]
                if ($l -match '\{') { $braceCount += ([regex]::Matches($l,'\{').Count); $foundStart = $true }
                if ($l -match '\}') { $braceCount -= ([regex]::Matches($l,'\}').Count) }
                if ($foundStart -and $braceCount -le 0) { $endIndex = $j; break }
            }
            if (-not $endIndex) { $endIndex = [math]::Min($startIndex+1000, $lines.Length-1) }

            $body = ($lines[$startIndex..$endIndex] -join "`n")

            $propMatches = @()
            foreach ($m in [regex]::Matches($body, $propertyRegex, 'Multiline')) { $propMatches += @{ Type = $m.Groups[3].Value.Trim(); Name = $m.Groups[4].Value.Trim() } }
            $methodMatches = @()
            foreach ($m in [regex]::Matches($body, $methodSigRegex, 'Multiline')) {
                $ret = $m.Groups[3].Value.Trim()
                $name = $m.Groups[4].Value.Trim()
                $args = $m.Groups[5].Value.Trim()
                if ($name -and ($name -notmatch 'get_|set_')) { $methodMatches += @{ Return = $ret; Name = $name; Args = $args } }
            }

            $isDto = ($typeName -match 'Dto$') -or ($currentNamespace -match '\.Dto(s)?$' -or $currentNamespace -match '\.Dtos$')
            $isEntity = ($typeName -match '(Entity$|Model$|Dto$)') -and -not $isDto

            $types += @{
                File = (Resolve-Path $file.FullName).ProviderPath.Substring($rootFull.Length).TrimStart('\','/');
                Namespace = $currentNamespace;
                Kind = $typeKind;
                Name = $typeName;
                Props = $propMatches;
                Methods = $methodMatches;
                IsDto = $isDto;
                IsEntity = $isEntity;
            }
        }
    }
}

# 检测简单 DTO 转换线索
$mappingHints = @()
foreach ($t in $types) { foreach ($m in $t.Methods) { if ($m.Name -match 'ToDto|FromDto|MapTo|MapFrom|ToModel|ToEntity') { $mappingHints += @{ Type = $t.Name; Method = $m.Name; Signature = "$($m.Return) $($m.Name)($($m.Args))" } } } }

# 生成 Markdown 输出
$md = @()
$md += "# PROJECT SUMMARY"
$md += ""
$md += "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
$md += ""
$md += "## Included Projects"
$md += ""
$md += "- " + ($IncludeProjects -join "`n- ")
$md += ""
$md += "## File Tree"
$md += ""
$md += '```'
$md += $tree
$md += '```'
$md += ""
$md += "## Types Overview"
$md += ""

foreach ($t in $types) {
    $md += '### ' + $t.Kind + ' ' + $t.Name + '  `(' + $t.Namespace + ')`'
    $md += ""
    $md += '- File: `' + $t.File + '`'
    if ($t.IsDto) { $md += '- Detected: DTO' }
    elseif ($t.IsEntity) { $md += '- Detected: Entity/Model' }
    $md += ""
    if ($t.Props.Count -gt 0) {
        $md += "#### Properties"
        $md += ""
        foreach ($p in $t.Props) { $md += '- `' + $p.Type + ' ' + $p.Name + '`' }
        $md += ""
    }
    if ($t.Methods.Count -gt 0) {
        $md += "#### Methods (signatures)"
        $md += ""
        foreach ($m in $t.Methods) { $ret = if ($m.Return) { $m.Return } else { "" }; $md += '- `' + $ret + ' ' + $m.Name + '(' + $m.Args + ')' + '`' }
        $md += ""
    }
}

if ($mappingHints.Count -gt 0) {
    $md += "## Detected Mapping Methods / DTO Conversions"
    $md += ""
    foreach ($h in $mappingHints) { $md += '- Type `' + $h.Type + '` -> Method: `' + $h.Signature + '`' }
    $md += ""
}

$md += "## Notes"
$md += ""
$md += "- UI designer files (`*.Designer.cs`) 已排除以减小输出体积。"
$md += "- 若希望包含 designer 文件，请传入扩展参数并修改脚本。"
$md += "- 解析基于简单正则与花括号计数，复杂语法建议用 Roslyn 精确解析。"
$md += ""

# 写入文件
$outPath = Join-Path $rootFull $OutFile
$md -join "`n" | Out-File -FilePath $outPath -Encoding UTF8

Write-Host "PROJECT_SUMMARY written to $outPath"