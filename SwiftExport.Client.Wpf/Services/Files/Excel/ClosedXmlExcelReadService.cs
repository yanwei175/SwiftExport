using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using SwiftExportClient.Services.Files.Internal;

namespace SwiftExportClient.Services.Files.Excel;

/// <summary>使用 ClosedXML 在后台读取有界 XLSX 缓存值快照，不重新计算公式，也不依赖安装 Excel。</summary>
public sealed class ClosedXmlExcelReadService : IExcelReadService
{
    /// <inheritdoc />
    public async Task<ExcelWorkbookInfo> InspectAsync(
        Stream source,
        FileReadLimits? limits = null,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        limits ??= FileReadLimits.Default;
        using var buffer = await FileInput.ReadBoundedAsync(source, limits, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => ReadWorkbook(buffer, limits, cancellationToken, workbook =>
        {
            progress?.Report(new FileOperationProgress("InspectExcel"));
            return new ExcelWorkbookInfo(workbook.Worksheets.Select(sheet => sheet.Name));
        }), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TableReadResult> ReadTableAsync(
        Stream source,
        string worksheetName,
        ExcelTableReadOptions? options = null,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateWorksheetName(worksheetName);
        options ??= new ExcelTableReadOptions();
        ValidateTableOptions(options);
        var culture = SnapshotCulture(options.Culture);
        using var buffer = await FileInput.ReadBoundedAsync(source, options.Limits, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => ReadWorkbook(buffer, options.Limits, cancellationToken, workbook =>
            ReadTable(GetWorksheet(workbook, worksheetName), options, culture, progress, cancellationToken)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ExcelLayoutReadResult> ReadLayoutAsync(
        Stream source,
        string worksheetName,
        ExcelLayoutDefinition layout,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ValidateWorksheetName(worksheetName);
        var fields = ValidateLayout(layout);
        var culture = SnapshotCulture(layout.Culture);
        using var buffer = await FileInput.ReadBoundedAsync(source, layout.Limits, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => ReadWorkbook(buffer, layout.Limits, cancellationToken, workbook =>
            ReadLayout(GetWorksheet(workbook, worksheetName), layout, fields, culture, progress, cancellationToken)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>在格式容器检查和加载之后执行读取；损坏文件被归类，取消和既有结构错误保持原类别。</summary>
    private static T ReadWorkbook<T>(MemoryStream source, FileReadLimits limits, CancellationToken token, Func<XLWorkbook, T> read)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            FileInput.ValidateXlsx(source, limits, token);
            using var workbook = new XLWorkbook(source);
            token.ThrowIfCancellationRequested();
            var result = read(workbook);
            token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not FileOperationException)
        {
            throw new FileOperationException("InvalidXlsx", "无法读取 XLSX 文件，请确认文件完整且格式受支持。", exception);
        }
    }

    /// <summary>从内容确定平表末行末列，样式空尾不扩大范围，中间空行和原列号保持不变。</summary>
    private static TableReadResult ReadTable(
        IXLWorksheet sheet, ExcelTableReadOptions options, CultureInfo culture,
        IProgress<FileOperationProgress>? progress, CancellationToken token)
    {
        var lastColumn = options.LastColumn ?? 0;
        if (!options.LastColumn.HasValue)
        {
            foreach (var cell in sheet.CellsUsed(XLCellsUsedOptions.Contents))
            {
                token.ThrowIfCancellationRequested();
                if (cell.Address.RowNumber >= options.HeaderRow && cell.Address.ColumnNumber >= options.FirstColumn)
                    lastColumn = Math.Max(lastColumn, cell.Address.ColumnNumber);
            }
        }
        if (lastColumn < options.FirstColumn)
            return new TableReadResult(sheet.Name, [], []);
        var lastRow = 0;
        foreach (var cell in sheet.CellsUsed(XLCellsUsedOptions.Contents))
        {
            token.ThrowIfCancellationRequested();
            if (cell.Address.ColumnNumber >= options.FirstColumn && cell.Address.ColumnNumber <= lastColumn
                && cell.Address.RowNumber >= options.DataStartRow)
                lastRow = Math.Max(lastRow, cell.Address.RowNumber);
        }
        var columnCount = lastColumn - options.FirstColumn + 1;
        var rowCount = Math.Max(0, lastRow - options.DataStartRow + 1);
        ValidateSnapshotSize(options.Limits, rowCount, columnCount, (long)(rowCount + 1) * columnCount);

        var issues = new List<FileIssue>();
        using var cells = new ExcelCellSnapshotReader(sheet, culture, issues);
        var headers = ReadTableRow(cells, options.HeaderRow, options.FirstColumn, lastColumn, token);
        var rows = new List<IReadOnlyList<FileCell>>(rowCount);
        progress?.Report(new FileOperationProgress("ReadExcelTable", 0, rowCount));
        for (var row = options.DataStartRow; row <= lastRow; row++)
        {
            token.ThrowIfCancellationRequested();
            rows.Add(ReadTableRow(cells, row, options.FirstColumn, lastColumn, token));
            ReportRows(progress, "ReadExcelTable", rows.Count, rowCount);
        }
        return new TableReadResult(sheet.Name, headers, rows, issues);
    }

    /// <summary>按地址定义分别读取一次性字段和向下字段，不删除空明细行，不根据字段键执行业务转换。</summary>
    private static ExcelLayoutReadResult ReadLayout(
        IXLWorksheet sheet, ExcelLayoutDefinition layout, LayoutField[] fields, CultureInfo culture,
        IProgress<FileOperationProgress>? progress, CancellationToken token)
    {
        var headerFields = fields.Where(field => !field.IsDownward).ToArray();
        var detailFields = fields.Where(field => field.IsDownward).ToArray();
        var issues = new List<FileIssue>();
        var startRow = detailFields.Length == 0 ? 1 : detailFields[0].Row;
        var lastRow = detailFields.Length == 0 ? 0 : FindEndRow(sheet, layout, startRow, issues, token);
        var rowCount = Math.Max(0, lastRow - startRow + 1);
        ValidateSnapshotSize(layout.Limits, rowCount, fields.Length, headerFields.Length + (long)rowCount * detailFields.Length);

        using var cells = new ExcelCellSnapshotReader(sheet, culture, issues);
        var header = new Dictionary<string, FileCell>(StringComparer.Ordinal);
        foreach (var field in headerFields)
        {
            token.ThrowIfCancellationRequested();
            header.Add(field.Key, cells.Read(field.Row, field.Column, resolveMergedAnchor: true, field.Key));
        }
        var rows = new List<IReadOnlyDictionary<string, FileCell>>(rowCount);
        progress?.Report(new FileOperationProgress("ReadExcelLayout", 0, rowCount));
        for (var row = startRow; row <= lastRow; row++)
        {
            token.ThrowIfCancellationRequested();
            var values = new Dictionary<string, FileCell>(StringComparer.Ordinal);
            foreach (var field in detailFields)
            {
                token.ThrowIfCancellationRequested();
                values.Add(field.Key, cells.Read(row, field.Column, resolveMergedAnchor: false, field.Key));
            }
            rows.Add(values);
            ReportRows(progress, "ReadExcelLayout", rows.Count, rowCount);
        }
        return new ExcelLayoutReadResult(sheet.Name, header, rows, issues);
    }

    /// <summary>末行判断只检查缓存值，零和错误值算有内容，空白文本和没有缓存的公式不延长明细。</summary>
    private static int FindEndRow(
        IXLWorksheet sheet, ExcelLayoutDefinition layout, int startRow, IList<FileIssue> issues, CancellationToken token)
    {
        if (layout.EndMode == ExcelLayoutEndMode.BeforeSummaryCell)
        {
            ExcelAddress.TryParseCell(layout.SummaryCellAddress, out var summaryRow, out _);
            return summaryRow - 1;
        }
        ExcelAddress.TryParseColumn(layout.LastRowColumn, out var column);
        var lastRow = startRow - 1;
        foreach (var cell in sheet.Column(column).CellsUsed(XLCellsUsedOptions.Contents))
        {
            token.ThrowIfCancellationRequested();
            var row = cell.Address.RowNumber;
            if (row < startRow)
                continue;
            var value = cell.CachedValue;
            if (cell.HasFormula && value.Type == XLDataType.Blank)
                issues.Add(new FileIssue("FormulaCacheMissing", "末行判断列的公式没有已有缓存，不作为非空末行。",
                    sheet.Name, row, column, ExcelAddress.Format(row, column)));
            if (value.Type != XLDataType.Blank && (value.Type != XLDataType.Text || !string.IsNullOrWhiteSpace(value.GetText())))
                lastRow = Math.Max(lastRow, row);
        }
        return lastRow;
    }

    private static IReadOnlyList<FileCell> ReadTableRow(ExcelCellSnapshotReader cells, int row, int first, int last, CancellationToken token)
    {
        var values = new FileCell[last - first + 1];
        for (var column = first; column <= last; column++)
        {
            token.ThrowIfCancellationRequested();
            values[column - first] = cells.Read(row, column, resolveMergedAnchor: false);
        }
        return values;
    }

    /// <summary>在分配完整结果集合前检查行、列和单元格总数；结果不返回被截断的部分数据。</summary>
    private static void ValidateSnapshotSize(FileReadLimits limits, int rows, int columns, long cellCount)
    {
        if (rows > limits.MaxRows || columns > limits.MaxColumns || cellCount > limits.MaxCells)
            throw new FileOperationException("ReadSnapshotTooLarge", "读取结果超过允许的行、列或单元格上限，请缩小读取范围。");
    }

    private static IXLWorksheet GetWorksheet(XLWorkbook workbook, string name) =>
        workbook.Worksheets.FirstOrDefault(sheet => string.Equals(sheet.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new FileOperationException("WorksheetNotFound", "指定工作表不存在，请重新选择工作表。");

    private static void ValidateWorksheetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new FileOperationException("WorksheetRequired", "请明确选择需要读取的工作表。");
    }

    private static CultureInfo SnapshotCulture(CultureInfo culture) => culture is null
        ? throw new FileOperationException("InvalidReadOptions", "读取显示文本的区域性不能为空。")
        : CultureInfo.ReadOnly((CultureInfo)culture.Clone());

    /// <summary>先验证显式表头和读取边界，错误配置不消耗调用方输入流。</summary>
    private static void ValidateTableOptions(ExcelTableReadOptions options)
    {
        if (options.HeaderRow < 1 || options.HeaderRow > ExcelAddress.MaximumRows
            || options.DataStartRow <= options.HeaderRow || options.DataStartRow > ExcelAddress.MaximumRows
            || options.FirstColumn < 1 || options.FirstColumn > ExcelAddress.MaximumColumns
            || options.LastColumn is { } last && (last < options.FirstColumn || last > ExcelAddress.MaximumColumns)
            || options.Limits is null)
            throw new FileOperationException("InvalidReadOptions", "平表表头、明细起点或列范围无效。");
    }

    /// <summary>冻结字段定义并验证单格地址、唯一字段键、共同明细起点及显式结束边界。</summary>
    private static LayoutField[] ValidateLayout(ExcelLayoutDefinition layout)
    {
        if (layout.Fields is null || layout.Fields.Count == 0 || layout.Limits is null)
            throw new FileOperationException("InvalidExcelLayout", "地址布局必须包含字段和有效读取限制。");
        var result = new List<LayoutField>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in layout.Fields.ToArray())
        {
            if (field is null || string.IsNullOrWhiteSpace(field.FieldKey) || !keys.Add(field.FieldKey)
                || !ExcelAddress.TryParseCell(field.CellAddress, out var row, out var column))
                throw new FileOperationException("InvalidExcelLayout", "字段键必须非空且唯一，读取地址必须是有效的单个 A1 地址。");
            result.Add(new LayoutField(field.FieldKey, row, column, field.IsDownward));
        }
        var starts = result.Where(field => field.IsDownward).Select(field => field.Row).Distinct().ToArray();
        if (starts.Length > 1)
            throw new FileOperationException("InvalidExcelLayout", "所有向下字段必须从同一行开始。");
        if (starts.Length == 1)
        {
            var validEnd = layout.EndMode switch
            {
                ExcelLayoutEndMode.LastNonEmptyColumn => ExcelAddress.TryParseColumn(layout.LastRowColumn, out _),
                ExcelLayoutEndMode.BeforeSummaryCell => ExcelAddress.TryParseCell(layout.SummaryCellAddress, out var summary, out _) && summary > starts[0],
                _ => false,
            };
            if (!validEnd)
                throw new FileOperationException("InvalidExcelLayout", "向下字段必须配置有效的末行判断列或起始行之后的汇总地址。");
        }
        return result.ToArray();
    }

    // 逐行检查取消，进度每 100 行及最后一行报告，避免几千行导入制造过多 UI 回调。
    private static void ReportRows(IProgress<FileOperationProgress>? progress, string stage, int completed, int total)
    {
        if (completed % 100 == 0 || completed == total)
            progress?.Report(new FileOperationProgress(stage, completed, total));
    }

    private sealed record LayoutField(string Key, int Row, int Column, bool IsDownward);
}
