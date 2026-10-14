using System.Globalization;
using System.IO;
using System.Reflection;
using ClosedXML.Excel;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Import;
using SwiftExportClient.Features.Products.ProductMasterImport.Models;
using SwiftExportClient.Services.Files;
using SwiftExportClient.Services.Files.Excel;
using SwiftExportClient.Services.Files.Templates;

namespace SwiftExportClient.Features.Products.ProductMasterImport.Services;

/// <summary>系统文件对话框已确认的输出位置及覆盖许可。</summary>
public sealed record ProductMasterImportFileTarget(string Path, bool Overwrite);

/// <summary>十一字段平表的模板、整文件读取校验及失败回写，无 API 或 UI 依赖。</summary>
public sealed class ProductMasterWorkbookService(IExcelReadService reader)
{
    /// <summary>模板和错误文件共用字段顺序；重导入只读取前十一列。</summary>
    public static readonly IReadOnlyList<(string Key, string Header, double Width)> Columns = Array.AsReadOnly(new[]
    {
        (nameof(ProductMasterImportRowDto.MasterSku), "主型号", 22d),
        (nameof(ProductMasterImportRowDto.DefaultDescriptionZh), "中文描述", 36d),
        (nameof(ProductMasterImportRowDto.DefaultDescriptionEn), "英文描述", 36d),
        (nameof(ProductMasterImportRowDto.DefaultFeaturesZh), "中文配置", 32d),
        (nameof(ProductMasterImportRowDto.DefaultFeaturesEn), "英文配置", 32d),
        (nameof(ProductMasterImportRowDto.DefaultPcsPerPackage), "每箱装率", 14d),
        (nameof(ProductMasterImportRowDto.DefaultNetWeight), "每箱净重(KG)", 18d),
        (nameof(ProductMasterImportRowDto.DefaultGrossWeight), "每箱毛重(KG)", 18d),
        (nameof(ProductMasterImportRowDto.DefaultCbm), "每箱体积(CBM)", 18d),
        (nameof(ProductMasterImportRowDto.ProductSeriesNameEn), "产品系列英文名称", 34d),
        (nameof(ProductMasterImportRowDto.DefaultSupplierCode), "默认供应商编码", 22d)
    });
    private static readonly PropertyInfo[] Fields = Columns.Select(column => typeof(ProductMasterImportRowDto).GetProperty(column.Key)!).ToArray();
    private static readonly FileReadLimits Limits = FileReadLimits.Default with { MaxRows = 1_048_575 };

    /// <summary>获取工作表名，保留公共容器与字节保护。</summary>
    public async Task<IReadOnlyList<string>> InspectAsync(string path, CancellationToken token)
    {
        await using var stream = Open(path);
        return (await reader.InspectAsync(stream, Limits, cancellationToken: token)).WorksheetNames;
    }

    /// <summary>后台读取完整文件并统一校验；任意行错误都由页面阻止上传，不截断数据。</summary>
    public Task<IReadOnlyList<ProductMasterImportRowModel>> ReadAsync(string path, string sheet, CancellationToken token) => Task.Run<IReadOnlyList<ProductMasterImportRowModel>>(async () =>
    {
        await using var stream = Open(path);
        var table = await reader.ReadTableAsync(stream, sheet, new ExcelTableReadOptions { LastColumn = 11, Limits = Limits }, cancellationToken: token);
        var mapping = new Dictionary<int, int>();
        foreach (var cell in table.Headers)
        {
            var index = Columns.Select(column => column.Header).ToList().IndexOf(cell.DisplayText.Trim());
            if (index < 0 || cell.Formula is not null || cell.MergedRange is not null || mapping.ContainsValue(index))
                throw new FileOperationException("InvalidImportHeader", "第一行的前十一列必须完整且不重复地包含模板字段，不能合并或使用公式。");
            mapping[cell.ColumnNumber] = index;
        }
        if (mapping.Count != 11) throw new FileOperationException("MissingImportHeader", "请使用完整的十一字段模板。");
        var rows = new List<ProductMasterImportRowModel>();
        foreach (var cells in table.Rows)
        {
            token.ThrowIfCancellationRequested();
            if (cells.All(cell => cell.Kind == FileCellKind.Blank && cell.Formula is null)) continue;
            var input = new ProductMasterImportRowDto { SourceRowNumber = cells[0].RowNumber };
            var errors = new List<string>();
            foreach (var cell in cells)
            {
                var index = mapping[cell.ColumnNumber];
                var numeric = index is >= 5 and <= 8;
                var text = numeric && cell.RawValue is double number ? number.ToString("R", CultureInfo.InvariantCulture) : cell.DisplayText;
                if (cell.Formula is not null && cell.Kind == FileCellKind.Blank) text = "=" + cell.Formula;
                Fields[index].SetValue(input, string.IsNullOrWhiteSpace(text) ? null : text.Trim());
                if (cell.Kind == FileCellKind.Error || cell.Formula is not null && cell.Kind == FileCellKind.Blank)
                    errors.Add($"{Columns[index].Header}含错误或公式缺少缓存，请计算保存或改为明确值");
                if (cell.MergedRange is not null) errors.Add($"{Columns[index].Header}不能使用合并单元格");
            }
            Validate(input, errors);
            rows.Add(new(input) { Reason = string.Join("；", errors), Status = errors.Count > 0 ? ProductMasterImportStatus.Failed : ProductMasterImportStatus.Pending });
        }
        foreach (var duplicate in rows.GroupBy(row => row.Input.MasterSku, StringComparer.OrdinalIgnoreCase).Where(group => group.Key is not null && group.Count() > 1))
            foreach (var row in duplicate)
            { row.Reason = string.Join("；", new[] { row.Reason, "文件中主型号重复" }.Where(value => value.Length > 0)); row.Status = ProductMasterImportStatus.Failed; }
        return rows;
    }, token);

    /// <summary>写空白模板或原数据加原因列；所有值作为文本写入，失败或取消不损坏已有输出。</summary>
    public Task WriteAsync(ProductMasterImportFileTarget target, IReadOnlyList<ProductMasterImportRowModel> rows,
        bool includeReason, CancellationToken token, Action<Action>? publishCommit = null) => Task.Run(async () =>
    {
        var full = AtomicTemplateFileWriter.ValidateTarget(target.Path, target.Overwrite);
        using var book = new XLWorkbook(); var sheet = book.AddWorksheet("父级型号");
        for (var index = 0; index < Columns.Count; index++)
        {
            var header = sheet.Cell(1, index + 1); header.Value = Columns[index].Header;
            header.Style.Font.Bold = true; header.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
            if (index == 0) header.Style.Font.FontColor = XLColor.Red;
            sheet.Column(index + 1).Width = Columns[index].Width;
            sheet.Column(index + 1).Style.NumberFormat.Format = "@";
        }
        if (includeReason) { sheet.Cell(1, 12).Value = "错误原因"; sheet.Column(12).Width = 60; }
        for (var index = 0; index < rows.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            for (var column = 0; column < Fields.Length; column++) sheet.Cell(index + 2, column + 1).Value = Fields[column].GetValue(rows[index].Input) as string ?? string.Empty;
            if (includeReason) sheet.Cell(index + 2, 12).Value = rows[index].Reason.Length > 0 ? rows[index].Reason : rows[index].StatusText;
        }
        sheet.Range(1, 1, Math.Max(2, rows.Count + 1), includeReason ? 12 : 11).Style.Alignment.WrapText = true;
        sheet.SheetView.FreezeRows(1); sheet.Range(1, 1, rows.Count + 1, includeReason ? 12 : 11).SetAutoFilter();
        using var content = new MemoryStream(); book.SaveAs(content, new SaveOptions { EvaluateFormulasBeforeSaving = false });
        await AtomicTemplateFileWriter.WriteAsync(content, full, target.Overwrite, null, token, publishCommit).ConfigureAwait(false);
    }, token);

    /// <summary>本地只校验文件字段和数值；系列英文名称与供应商的实时引用交给服务器。</summary>
    private static void Validate(ProductMasterImportRowDto row, List<string> errors)
    {
        if (row.MasterSku is null) errors.Add("主型号不能为空");
        var maximums = new[] { 300, 2000, 2000, 500, 500, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, 500, 300 };
        for (var index = 0; index < Fields.Length; index++)
            if ((Fields[index].GetValue(row) as string)?.Length > maximums[index]) errors.Add($"{Columns[index].Header}不能超过 {maximums[index]} 字符");
        if (row.DefaultPcsPerPackage is { } pcs && (!int.TryParse(pcs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) || integer <= 0)) errors.Add("每箱装率须为正整数或空白");
        Measure(row.DefaultNetWeight, 4, "每箱净重"); Measure(row.DefaultGrossWeight, 4, "每箱毛重"); Measure(row.DefaultCbm, 6, "每箱体积");
        void Measure(string? value, int scale, string name)
        {
            if (value is null) return;
            if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number < 0
                || number >= (scale == 4 ? 100_000_000_000_000m : 1_000_000_000_000m) || decimal.Round(number, scale) != number)
                errors.Add($"{name}须为非负数，最多 {scale} 位小数且不超数据库容量");
        }
    }
    /// <summary>只接收标准 XLSX，真实内容检查仍由公共读取器负责。</summary>
    private static FileStream Open(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new FileOperationException("UnsupportedImportFormat", "父级导入仅支持 XLSX。");
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
    }
}
