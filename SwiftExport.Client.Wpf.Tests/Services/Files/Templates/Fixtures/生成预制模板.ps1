# 生成固定的 Open XML 模板夹具；测试直接读取生成后的 XLSX，不运行本脚本或复制服务渲染逻辑。
$ErrorActionPreference = 'Stop'

function New-TextCell([string] $Address, [string] $Text, [int] $Style = 0) {
    $escapedText = [System.Security.SecurityElement]::Escape($Text)
    return "<c r=`"$Address`" s=`"$Style`" t=`"inlineStr`"><is><t xml:space=`"preserve`">$escapedText</t></is></c>"
}

function New-NumberCell([string] $Address, [string] $Number) {
    return "<c r=`"$Address`"><v>$Number</v></c>"
}

function New-FormulaCell([string] $Address, [string] $Formula) {
    $escapedFormula = [System.Security.SecurityElement]::Escape($Formula)
    return "<c r=`"$Address`"><f>$escapedFormula</f></c>"
}

function New-Row([int] $Index, [string[]] $Cells) {
    return "<row r=`"$Index`">$($Cells -join '')</row>"
}

# 每份夹具使用相同有效包骨架，单元格地址、命名区域和预期布局均在下方固定声明。
function Write-Template([string] $Name, [string] $SheetName, [string[]] $Rows, [string] $DefinedNames, [string] $Merge = '') {
    $parts = @{}
    $parts['[Content_Types].xml'] = @'
<?xml version="1.0" encoding="utf-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>
'@
    $parts['_rels/.rels'] = @'
<?xml version="1.0" encoding="utf-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
'@
    $parts['xl/_rels/workbook.xml.rels'] = @'
<?xml version="1.0" encoding="utf-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
'@
    $parts['xl/styles.xml'] = @'
<?xml version="1.0" encoding="utf-8"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="1"><numFmt numFmtId="164" formatCode="#,##0.00"/></numFmts><fonts count="2"><font><sz val="11"/><name val="Calibri"/><family val="2"/></font><font><b/><sz val="14"/><name val="Calibri"/><family val="2"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
'@
    $parts['xl/workbook.xml'] = "<?xml version=`"1.0`" encoding=`"utf-8`"?><workbook xmlns=`"http://schemas.openxmlformats.org/spreadsheetml/2006/main`" xmlns:r=`"http://schemas.openxmlformats.org/officeDocument/2006/relationships`"><bookViews><workbookView/></bookViews><sheets><sheet name=`"$SheetName`" sheetId=`"1`" r:id=`"rId1`"/></sheets><definedNames>$DefinedNames<definedName name=`"_xlnm.Print_Titles`" localSheetId=`"0`">'$SheetName'!`$4:`$4</definedName></definedNames><calcPr calcId=`"191029`" calcMode=`"manual`"/></workbook>"
    $parts['xl/worksheets/sheet1.xml'] = "<?xml version=`"1.0`" encoding=`"utf-8`"?><worksheet xmlns=`"http://schemas.openxmlformats.org/spreadsheetml/2006/main`"><sheetViews><sheetView workbookViewId=`"0`"/></sheetViews><sheetFormatPr defaultRowHeight=`"18`"/><cols><col min=`"2`" max=`"2`" width=`"22`" customWidth=`"1`"/><col min=`"3`" max=`"8`" width=`"16`" customWidth=`"1`"/></cols><sheetData>$($Rows -join '')</sheetData>$Merge<pageMargins left=`"0.3`" right=`"0.3`" top=`"0.5`" bottom=`"0.5`" header=`"0.2`" footer=`"0.2`"/><pageSetup paperSize=`"9`" orientation=`"landscape`" fitToWidth=`"1`" fitToHeight=`"0`"/></worksheet>"

    $targetPath = Join-Path $PSScriptRoot $Name
    $file = [System.IO.File]::Open($targetPath, [System.IO.FileMode]::Create)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new($file, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($part in $parts.GetEnumerator()) {
                $entry = $archive.CreateEntry($part.Key)
                $writer = [System.IO.StreamWriter]::new($entry.Open(), [System.Text.UTF8Encoding]::new($false))
                try { $writer.Write($part.Value) } finally { $writer.Dispose() }
            }
        } finally { $archive.Dispose() }
    } finally { $file.Dispose() }
}

$detailRows = @(
    (New-Row 1 @((New-TextCell 'B1' '{{Title}}' 1)))
    (New-Row 2 @((New-TextCell 'B2' '{{Order.CustomerName}}'), (New-NumberCell 'D2' '2'), (New-FormulaCell 'E2' 'SUM(E5:E5)')))
    (New-Row 3 @((New-TextCell 'B3' '{{Note}}')))
    (New-Row 4 @((New-TextCell 'B4' '编号'), (New-TextCell 'C4' '数量'), (New-TextCell 'D4' '单价'), (New-TextCell 'E4' '金额'), (New-TextCell 'F4' '备注')))
    (New-Row 5 @((New-TextCell 'B5' '{{item.Code}}'), (New-TextCell 'C5' '{{item.Quantity}}'), (New-TextCell 'D5' '{{item.Price}}' 2), (New-TextCell 'E5' '&=ROUND(C5*D5,2)' 2), (New-TextCell 'F5' '{{item.Text}}'), (New-TextCell 'G5' '&=C5*$D$2')))
    (New-Row 6 @())
    (New-Row 8 @((New-TextCell 'B8' '固定页脚')))
)
Write-Template '头与明细模板.xlsx' '订单' $detailRows '<definedName name="Lines">订单!$A$5:$G$6</definedName><definedName name="_xlnm.Print_Area" localSheetId="0">订单!$A$1:$G$8</definedName>'

$groupRows = @(
    (New-Row 1 @((New-TextCell 'B1' '{{Title}}' 1)))
    (New-Row 3 @((New-TextCell 'B3' '分类'), (New-TextCell 'C3' '编号'), (New-TextCell 'D3' '数量'), (New-TextCell 'E3' '单价'), (New-TextCell 'F3' '金额')))
    (New-Row 4 @((New-TextCell 'B4' '{{item.Name}}' 1)))
    (New-Row 5 @((New-TextCell 'B5' '{{item.GroupName}}'), (New-TextCell 'C5' '{{item.Code}}'), (New-TextCell 'D5' '{{item.Quantity}}'), (New-TextCell 'E5' '{{item.Price}}' 2), (New-TextCell 'F5' '&=ROUND(D5*E5,2)' 2)))
    (New-Row 6 @((New-TextCell 'B6' '<<group>>'), (New-TextCell 'F6' '<<sum>>')))
    (New-Row 7 @((New-TextCell 'B7' '本组结束')))
    (New-Row 8 @())
)
Write-Template '嵌套分组模板.xlsx' '分组' $groupRows '<definedName name="Groups">分组!$A$4:$F$8</definedName><definedName name="Groups_Items">分组!$A$5:$F$6</definedName>'

$formulaRows = @(
    (New-Row 1 @((New-TextCell 'B1' '{{Title}}' 1)))
    (New-Row 2 @((New-NumberCell 'B2' '1.25'), (New-FormulaCell 'F2' 'UNSUPPORTED_FUNCTION(1)')))
    (New-Row 3 @((New-TextCell 'B3' '编号'), (New-TextCell 'C3' '数量'), (New-TextCell 'D3' '金额'), (New-TextCell 'E3' '保留公式')))
    (New-Row 4 @((New-TextCell 'B4' '{{item.Code}}'), (New-TextCell 'C4' '{{item.Quantity}}'), (New-TextCell 'D4' '&=ROUND(C4*$B$2,2)' 2), (New-TextCell 'E4' '&=UNSUPPORTED_FUNCTION(C4)')))
    (New-Row 5 @())
)
Write-Template '公式打印模板.xlsx' '公式' $formulaRows '<definedName name="Items">公式!$A$4:$E$5</definedName>' '<mergeCells count="1"><mergeCell ref="B1:E1"/></mergeCells>'
