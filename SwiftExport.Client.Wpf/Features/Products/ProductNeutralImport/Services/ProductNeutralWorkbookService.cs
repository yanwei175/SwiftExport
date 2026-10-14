using System.Globalization;
using System.IO;
using System.Reflection;
using ClosedXML.Excel;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Import;
using SwiftExportClient.Features.Products.ProductNeutralImport.Models;
using SwiftExportClient.Services.Files;
using SwiftExportClient.Services.Files.Excel;
using SwiftExportClient.Services.Files.Templates;

namespace SwiftExportClient.Features.Products.ProductNeutralImport.Services;

/// <summary>系统保存对话框确认的输出位置及覆盖许可，只用于本功能的命令输入。</summary>
public sealed record ProductNeutralImportFileTarget(string Path, bool Overwrite);

/// <summary>八字段平表的模板、整文件校验及失败数据回写，不依赖 API 或业务页面。</summary>
public sealed class ProductNeutralWorkbookService(IExcelReadService reader)
{
    /// <summary>模板、输入与错误回写共用字段口径；识别第一行，不读取末尾旧原因。</summary>
    public static readonly IReadOnlyList<(string Key, string Header, double Width)> Columns = Array.AsReadOnly(new[]
    {
        (nameof(ProductNeutralImportRowDto.MasterSku), "主型号", 22d),
        (nameof(ProductNeutralImportRowDto.ProductCode), "中性产品编码", 24d),
        (nameof(ProductNeutralImportRowDto.ColorCodeUnique), "颜色唯一编码", 24d),
        (nameof(ProductNeutralImportRowDto.DefaultPurchasePriceCny), "默认人民币采购价", 22d),
        (nameof(ProductNeutralImportRowDto.DefaultSalesPriceUsd), "默认美元销售价", 22d),
        (nameof(ProductNeutralImportRowDto.DefaultExchangeRate), "默认汇率", 18d),
        (nameof(ProductNeutralImportRowDto.ProfitMargin), "利润率", 18d),
        (nameof(ProductNeutralImportRowDto.ImagePath), "图片地址", 60d)
    });
    private static readonly PropertyInfo[] Fields = Columns.Select(column => typeof(ProductNeutralImportRowDto).GetProperty(column.Key)!).ToArray();
    private static readonly FileReadLimits Limits = FileReadLimits.Default with { MaxRows = 1_048_575 };

    /// <summary>使用公共容量和容器检查获取可选工作表。</summary>
    public async Task<IReadOnlyList<string>> InspectAsync(string path, CancellationToken token)
    {
        await using var stream = Open(path);
        return (await reader.InspectAsync(stream, Limits, cancellationToken: token)).WorksheetNames;
    }

    /// <summary>后台读取完整八列快照，校验格式及重复编码，不进行服务器预览或引用猜测。</summary>
    public Task<IReadOnlyList<ProductNeutralImportRowModel>> ReadAsync(string path, string sheet, CancellationToken token) => Task.Run<IReadOnlyList<ProductNeutralImportRowModel>>(async () =>
    {
        await using var stream = Open(path);
        var table = await reader.ReadTableAsync(stream, sheet, new ExcelTableReadOptions { LastColumn = 8, Limits = Limits }, cancellationToken: token);
        var mapping = new Dictionary<int, int>();
        foreach (var cell in table.Headers)
        {
            var index = Columns.Select(column => column.Header).ToList().IndexOf(cell.DisplayText.Trim());
            if (index < 0 || cell.Formula is not null || cell.MergedRange is not null || mapping.ContainsValue(index))
                throw new FileOperationException("InvalidImportHeader", "第一行的前八列必须完整且不重复地包含模板字段，不能合并或使用公式。");
            mapping[cell.ColumnNumber] = index;
        }
        if (mapping.Count != 8) throw new FileOperationException("MissingImportHeader", "请使用完整的八字段中性产品模板。");
        var rows = new List<ProductNeutralImportRowModel>();
        foreach (var cells in table.Rows)
        {
            token.ThrowIfCancellationRequested();
            if (cells.All(cell => cell.Kind == FileCellKind.Blank && cell.Formula is null)) continue;
            var input = new ProductNeutralImportRowDto { SourceRowNumber = cells[0].RowNumber };
            var errors = new List<string>();
            foreach (var cell in cells)
            {
                var index = mapping[cell.ColumnNumber];
                var text = index is >= 3 and <= 6 && cell.RawValue is double number ? number.ToString("R", CultureInfo.InvariantCulture) : cell.DisplayText;
                if (cell.Formula is not null && cell.Kind == FileCellKind.Blank) text = "=" + cell.Formula;
                Fields[index].SetValue(input, string.IsNullOrWhiteSpace(text) ? null : text.Trim());
                if (cell.Kind == FileCellKind.Error || cell.Formula is not null && cell.Kind == FileCellKind.Blank)
                    errors.Add($"{Columns[index].Header}含错误或公式缺少缓存，请计算保存或改为明确值");
                if (cell.MergedRange is not null) errors.Add($"{Columns[index].Header}不能使用合并单元格");
            }
            Validate(input, errors);
            rows.Add(new(input) { Reason = string.Join("；", errors), Status = errors.Count > 0 ? ProductNeutralImportStatus.Failed : ProductNeutralImportStatus.Pending });
        }
        foreach (var duplicate in rows.GroupBy(row => row.Input.ProductCode, StringComparer.OrdinalIgnoreCase).Where(group => group.Key is not null && group.Count() > 1))
            foreach (var row in duplicate)
            { row.Reason = string.Join("；", new[] { row.Reason, "文件中中性产品编码重复" }.Where(value => value.Length > 0)); row.Status = ProductNeutralImportStatus.Failed; }
        return rows;
    }, token);

    /// <summary>生成空白模板或原数据加原因列，字面文本防止公式注入，最终同目录原子发布。</summary>
    public Task WriteAsync(ProductNeutralImportFileTarget target, IReadOnlyList<ProductNeutralImportRowModel> rows,
        bool includeReason, CancellationToken token, Action<Action>? publishCommit = null) => Task.Run(async () =>
    {
        var full = AtomicTemplateFileWriter.ValidateTarget(target.Path, target.Overwrite);
        using var book = new XLWorkbook(); var sheet = book.AddWorksheet("中性产品");
        for (var index = 0; index < Columns.Count; index++)
        {
            var header = sheet.Cell(1, index + 1); header.Value = Columns[index].Header;
            header.Style.Font.Bold = true; header.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
            if (index < 3) header.Style.Font.FontColor = XLColor.Red;
            sheet.Column(index + 1).Width = Columns[index].Width;
            sheet.Column(index + 1).Style.NumberFormat.Format = "@";
        }
        sheet.Cell(1, 7).CreateComment().AddText("利润率填写 0.2 表示 20%；价格和汇率直接保存，不自动计算。");
        if (includeReason) { sheet.Cell(1, 9).Value = "错误原因"; sheet.Column(9).Width = 60; }
        for (var index = 0; index < rows.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            for (var column = 0; column < Fields.Length; column++) sheet.Cell(index + 2, column + 1).Value = Fields[column].GetValue(rows[index].Input) as string ?? string.Empty;
            if (includeReason) sheet.Cell(index + 2, 9).Value = rows[index].Reason.Length > 0 ? rows[index].Reason : rows[index].StatusText;
        }
        sheet.Range(1, 1, Math.Max(2, rows.Count + 1), includeReason ? 9 : 8).Style.Alignment.WrapText = true;
        sheet.SheetView.FreezeRows(1); sheet.Range(1, 1, rows.Count + 1, includeReason ? 9 : 8).SetAutoFilter();
        using var content = new MemoryStream(); book.SaveAs(content, new SaveOptions { EvaluateFormulasBeforeSaving = false });
        await AtomicTemplateFileWriter.WriteAsync(content, full, target.Overwrite, null, token, publishCommit).ConfigureAwait(false);
    }, token);

    /// <summary>本地统一校验原始字段，主型号和颜色的实时引用由服务端加锁复核。</summary>
    private static void Validate(ProductNeutralImportRowDto row, List<string> errors)
    {
        var maximums = new[] { 300, 300, 300, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, 1000 };
        for (var index = 0; index < Fields.Length; index++)
        {
            var value = Fields[index].GetValue(row) as string;
            if (index < 3 && value is null) errors.Add($"{Columns[index].Header}不能为空");
            if (value?.Length > maximums[index]) errors.Add($"{Columns[index].Header}不能超过 {maximums[index]} 字符");
        }
        Number(row.DefaultPurchasePriceCny, 4, "默认人民币采购价"); Number(row.DefaultSalesPriceUsd, 4, "默认美元销售价");
        Number(row.DefaultExchangeRate, 6, "默认汇率", positive: true); Number(row.ProfitMargin, 4, "利润率", margin: true);
        void Number(string? value, int scale, string name, bool positive = false, bool margin = false)
        {
            if (value is null) return;
            if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || (positive ? number <= 0 : number < 0) || margin && number >= 1
                || number >= (scale == 4 ? 100_000_000_000_000m : 1_000_000_000_000m) || decimal.Round(number, scale) != number)
                errors.Add($"{name}须为{(margin ? "大于等于零且小于一" : positive ? "正数" : "非负数")}，最多 {scale} 位小数且不超数据库容量");
        }
    }
    /// <summary>只接受标准 XLSX，宏、压缩容量及真实容器检查继续由公共读取器负责。</summary>
    private static FileStream Open(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new FileOperationException("UnsupportedImportFormat", "中性产品导入仅支持 XLSX。");
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
    }
}
