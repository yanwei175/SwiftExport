using System.Globalization;
using ClosedXML.Excel;

namespace SwiftExportClient.Services.Files.Excel;

/// <summary>从公式缓存创建带来源的纯值快照；显示格式使用无公式的临时工作簿，避免触发源工作簿计算。</summary>
internal sealed class ExcelCellSnapshotReader : IDisposable
{
    private readonly IXLWorksheet _worksheet;
    private readonly CultureInfo _culture;
    private readonly IList<FileIssue> _issues;
    private readonly XLWorkbook _formatWorkbook = new();
    private readonly IXLCell _formatCell;
    private readonly MergeArea[] _mergedAreas;

    /// <summary>每次读取独立创建格式化单元格，服务实例不持有跨请求的工作簿状态。</summary>
    internal ExcelCellSnapshotReader(IXLWorksheet worksheet, CultureInfo culture, IList<FileIssue> issues)
    {
        _worksheet = worksheet;
        _culture = culture;
        _issues = issues;
        _formatCell = _formatWorkbook.AddWorksheet("Format").Cell(1, 1);
        _mergedAreas = worksheet.MergedRanges.Select(range => new MergeArea(
            range.RangeAddress.FirstAddress.RowNumber,
            range.RangeAddress.FirstAddress.ColumnNumber,
            range.RangeAddress.LastAddress.RowNumber,
            range.RangeAddress.LastAddress.ColumnNumber)).ToArray();
    }

    /// <summary>固定字段允许取合并锚点；明细和平表保留原格值并报告合并区域，不扩散共享指标。</summary>
    internal FileCell Read(int row, int column, bool resolveMergedAnchor, string? fieldKey = null)
    {
        var merged = _mergedAreas.FirstOrDefault(area =>
            row >= area.FirstRow && row <= area.LastRow && column >= area.FirstColumn && column <= area.LastColumn);
        var sourceRow = resolveMergedAnchor && merged is not null ? merged.FirstRow : row;
        var sourceColumn = resolveMergedAnchor && merged is not null ? merged.FirstColumn : column;
        var cell = _worksheet.Cell(sourceRow, sourceColumn);
        var value = cell.CachedValue;
        var address = ExcelAddress.Format(row, column);
        if (cell.HasFormula && value.Type == XLDataType.Blank)
            _issues.Add(new FileIssue("FormulaCacheMissing", "公式没有已有缓存结果，读取值保持为空。",
                _worksheet.Name, row, column, address, fieldKey));
        if (value.Type == XLDataType.Error)
            _issues.Add(new FileIssue("ExcelCellError", "单元格包含 Excel 错误值，请核对源文件。",
                _worksheet.Name, row, column, address, fieldKey, FileIssueSeverity.Error));
        if (merged is not null && !resolveMergedAnchor)
            _issues.Add(new FileIssue("MergedDetailCell", "读取区域存在合并单元格，保留原格缓存值，不自动填充其他格。",
                _worksheet.Name, row, column, address, fieldKey));

        // 临时格只写缓存值和数字格式，禁止读取源格 Value/GetFormattedString 而触发公式重算。
        _formatCell.Value = value;
        if (string.IsNullOrEmpty(cell.Style.NumberFormat.Format))
            _formatCell.Style.NumberFormat.NumberFormatId = cell.Style.NumberFormat.NumberFormatId;
        else
            _formatCell.Style.NumberFormat.Format = cell.Style.NumberFormat.Format;
        var display = _formatCell.GetFormattedString(_culture);
        var (kind, raw) = ReadRawValue(value);
        return new FileCell(row, column, kind, raw, display)
        {
            Address = address,
            SourceAddress = ExcelAddress.Format(sourceRow, sourceColumn),
            Formula = cell.HasFormula ? cell.FormulaA1 : null,
            NumberFormat = cell.Style.NumberFormat.Format,
            NumberFormatId = cell.Style.NumberFormat.NumberFormatId,
            MergedRange = merged is null ? null :
                ExcelAddress.Format(merged.FirstRow, merged.FirstColumn) + ":" + ExcelAddress.Format(merged.LastRow, merged.LastColumn),
        };
    }

    /// <summary>只释放本次读取创建的格式化工作簿，不拥有输入工作表或调用方流。</summary>
    public void Dispose() => _formatWorkbook.Dispose();

    // 错误值转换成稳定文本，其他原值仅使用 .NET 基础类型，不泄漏 ClosedXML 枚举或对象。
    private static (FileCellKind Kind, object? Value) ReadRawValue(XLCellValue value) => value.Type switch
    {
        XLDataType.Blank => (FileCellKind.Blank, null),
        XLDataType.Text => (FileCellKind.Text, value.GetText()),
        XLDataType.Number => (FileCellKind.Number, value.GetNumber()),
        XLDataType.Boolean => (FileCellKind.Boolean, value.GetBoolean()),
        XLDataType.DateTime => (FileCellKind.DateTime, value.GetDateTime()),
        XLDataType.TimeSpan => (FileCellKind.TimeSpan, value.GetTimeSpan()),
        XLDataType.Error => (FileCellKind.Error, value.GetError().ToString()),
        _ => throw new FileOperationException("UnsupportedCellValue", "单元格值类型无法读取。"),
    };

    private sealed record MergeArea(int FirstRow, int FirstColumn, int LastRow, int LastColumn);
}
