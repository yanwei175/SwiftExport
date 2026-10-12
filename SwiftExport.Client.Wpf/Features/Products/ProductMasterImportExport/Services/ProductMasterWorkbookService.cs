using System.Globalization;
using System.IO;
using System.Reflection;
using ClosedXML.Excel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport;
using SwiftExportClient.Features.Products.ProductMasterImportExport.Models;
using SwiftExportClient.Services.Files;
using SwiftExportClient.Services.Files.Excel;
using SwiftExportClient.Services.Files.Templates;
namespace SwiftExportClient.Features.Products.ProductMasterImportExport.Services;
/// <summary>按同一十一字段定义读取并生成平表，无 API、弹窗、VSTO 或 Report 插值依赖。</summary>
public sealed class ProductMasterWorkbookService(IExcelReadService reader)
{
    /// <summary>主型号的模板识别和页面列名；本 Feature 拥有文案。</summary>
    public const string MasterSkuHeader = "主型号";
    /// <summary>中文描述的模板识别和页面列名；本 Feature 拥有文案。</summary>
    public const string DefaultDescriptionZhHeader = "中文描述";
    /// <summary>英文描述的模板识别和页面列名；本 Feature 拥有文案。</summary>
    public const string DefaultDescriptionEnHeader = "英文描述";
    /// <summary>中文配置的模板识别和页面列名；本 Feature 拥有文案。</summary>
    public const string DefaultFeaturesZhHeader = "中文配置";
    /// <summary>英文配置的模板识别和页面列名；本 Feature 拥有文案。</summary>
    public const string DefaultFeaturesEnHeader = "英文配置";
    /// <summary>每箱装率的模板识别和页面列名；本 Feature 拥有文案。</summary>
    public const string DefaultPcsPerPackageHeader = "每箱装率";
    /// <summary>每箱净重(KG)的模板识别和页面列名；本 Feature 拥有文案。</summary>
    public const string DefaultNetWeightHeader = "每箱净重(KG)";
    /// <summary>每箱毛重(KG)的模板识别和页面列名；本 Feature 拥有文案。</summary>
    public const string DefaultGrossWeightHeader = "每箱毛重(KG)";
    /// <summary>每箱体积(CBM)的模板识别和页面列名；本 Feature 拥有文案。</summary>
    public const string DefaultCbmHeader = "每箱体积(CBM)";
    /// <summary>分类路径的模板识别和页面列名；本 Feature 拥有文案。</summary>
    public const string CategoryPathHeader = "分类路径";
    /// <summary>默认供应商编码的模板识别和页面列名；本 Feature 拥有文案。</summary>
    public const string DefaultSupplierCodeHeader = "默认供应商编码";
    /// <summary>固定字段与显示名称，模板、识别和导出共用。</summary>
    public static readonly IReadOnlyList<(string Key,string Header,double Width)> Columns = Array.AsReadOnly(new[]
    {
        (nameof(ProductMasterImportRowInput.MasterSku), MasterSkuHeader, 22d),
        (nameof(ProductMasterImportRowInput.DefaultDescriptionZh), DefaultDescriptionZhHeader, 36d),
        (nameof(ProductMasterImportRowInput.DefaultDescriptionEn), DefaultDescriptionEnHeader, 36d),
        (nameof(ProductMasterImportRowInput.DefaultFeaturesZh), DefaultFeaturesZhHeader, 32d),
        (nameof(ProductMasterImportRowInput.DefaultFeaturesEn), DefaultFeaturesEnHeader, 32d),
        (nameof(ProductMasterImportRowInput.DefaultPcsPerPackage), DefaultPcsPerPackageHeader, 14d),
        (nameof(ProductMasterImportRowInput.DefaultNetWeight), DefaultNetWeightHeader, 18d),
        (nameof(ProductMasterImportRowInput.DefaultGrossWeight), DefaultGrossWeightHeader, 18d),
        (nameof(ProductMasterImportRowInput.DefaultCbm), DefaultCbmHeader, 18d),
        (nameof(ProductMasterImportRowInput.CategoryPath), CategoryPathHeader, 34d),
        (nameof(ProductMasterImportRowInput.DefaultSupplierCode), DefaultSupplierCodeHeader, 22d),
    });
    /// <summary>可编辑输入键，不包含身份或旧类型标签。</summary>
    public static readonly IReadOnlySet<string> InputKeys = Columns.Select(column => column.Key).ToHashSet(StringComparer.Ordinal);
    /// <summary>稳定识别字段对应的显示名称。</summary>
    public static string FieldTitle(string key) => key switch { nameof(ProductMasterTransferData.CategoryId) => "分类", nameof(ProductMasterTransferData.DefaultSupplierId) => "供应商", _ => Columns.FirstOrDefault(column => column.Key == key).Header ?? key };
    /// <summary>读取工作表名称，不持有打开的文件。</summary>
    public Task<IReadOnlyList<string>> InspectAsync(string path, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        await using var stream = Open(path);
        return (await reader.InspectAsync(stream, cancellationToken: cancellationToken)).WorksheetNames;
    }, cancellationToken);
    /// <summary>按第一行识别，保留行号及不可转换的输入，不读取公式 Value 或重新计算。</summary>
    public Task<(IReadOnlyList<ProductMasterImportLineModel> Rows, string Warning)> ReadAsync(string path, string sheet, CancellationToken cancellationToken)
    {
        // 文件打开及快照转换也放在后台，UI 线程仅接纳完整结果。
        return Task.Run<(IReadOnlyList<ProductMasterImportLineModel> Rows, string Warning)>(async () =>
        {
        await using var stream = Open(path);
        var table = await reader.ReadTableAsync(stream, sheet, cancellationToken: cancellationToken);
        var mapped = new Dictionary<int,string>();
        var unknown = false;
        foreach (var header in table.Headers)
        {
            var title = header.DisplayText.Trim();
            var field = Columns.FirstOrDefault(column => column.Header == title);
            if (field.Key is null) { if (title.Length > 0) unknown = true; continue; }
            if (mapped.ContainsValue(field.Key)) throw new FileOperationException("DuplicateImportHeader", "存在重复识别字段，请修正第一行。");
            if (header.Formula is not null || header.MergedRange is not null) throw new FileOperationException("InvalidImportHeader", "识别表头不能包含公式或合并单元格。");
            mapped.Add(header.ColumnNumber, field.Key);
        }
        if (!mapped.ContainsValue(nameof(ProductMasterImportRowInput.MasterSku))) throw new FileOperationException("MissingImportHeader", "第一行缺少主型号字段。");
        var result = new List<ProductMasterImportLineModel>();
        foreach (var cells in table.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cells.All(cell => cell.Kind == FileCellKind.Blank && cell.Formula is null)) continue;
            if (result.Count >= 1000) throw new FileOperationException("ImportRowLimitExceeded", "导入超过 1000 行，请拆分文件。");
            var input = new ProductMasterImportRowInput { SourceRowNumber = cells[0].RowNumber };
            // 缺失可选列统一为空字符串，避免契约默认 NULL 覆盖行模型的空文本初始值。
            foreach (var column in Columns) typeof(ProductMasterImportRowInput).GetProperty(column.Key)!.SetValue(input, string.Empty);
            var errors = new List<(string Key,string Message)>();
            foreach (var cell in cells)
            {
                if (!mapped.TryGetValue(cell.ColumnNumber, out var key)) continue;
                var numeric = key is nameof(input.DefaultPcsPerPackage) or nameof(input.DefaultNetWeight) or nameof(input.DefaultGrossWeight) or nameof(input.DefaultCbm);
                var text = numeric && cell.RawValue is double number ? number.ToString("R", CultureInfo.InvariantCulture) : cell.DisplayText;
                typeof(ProductMasterImportRowInput).GetProperty(key)!.SetValue(input, text);
                if (cell.Kind == FileCellKind.Error || (cell.Formula is not null && cell.Kind == FileCellKind.Blank))
                    errors.Add((key, "单元格错误或公式缺少已有缓存，请先计算并保存，或直接修正输入。"));
                if (cell.MergedRange is not null) errors.Add((key,"平表数据不能使用合并单元格，请拆开并填写每行值。"));
            }
            var line = input.Adapt<ProductMasterImportLineModel>();
            foreach (var error in errors) line.AddFileError(error.Key, error.Message);
            result.Add(line);
        }
        return (result, unknown ? "已忽略未识别列；类型、标签不参与本次导入。" : string.Empty);
        }, cancellationToken);
    }
    /// <summary>生成空白模板，不填入可能被误提交的示例记录。</summary>
    public Task WriteTemplateAsync(ProductMasterFileTarget target, CancellationToken cancellationToken = default) => WriteAsync(target, [], cancellationToken);
    /// <summary>将服务器已保存数据写入可重新导入的 XLSX，文本永不成为公式。</summary>
    public Task WriteAsync(ProductMasterFileTarget target, IReadOnlyList<ProductMasterExportRowDto> rows, CancellationToken cancellationToken = default, Action<Action>? publishCommit = null)
    {
        if (rows.Count > 10000) throw new FileOperationException("ExportRowLimitExceeded", "导出超过 10000 行。");
        return Task.Run(async () =>
        {
            var full = AtomicTemplateFileWriter.ValidateTarget(target.Path, target.Overwrite);
            using var book = new XLWorkbook();
            var sheet = book.AddWorksheet("父级型号");
            for (var column = 0; column < Columns.Count; column++)
            {
                var definition = Columns[column];
                var header = sheet.Cell(1,column+1);
                header.Value = definition.Header;
                header.Style.Font.Bold = true;
                header.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
                if (definition.Key == nameof(ProductMasterImportRowInput.MasterSku)) header.Style.Font.FontColor = XLColor.Red;
                sheet.Column(column+1).Width = definition.Width;
                var isNumeric = definition.Key.StartsWith("Default", StringComparison.Ordinal) && !definition.Key.Contains("Description") && !definition.Key.Contains("Features") && !definition.Key.Contains("Supplier");
                sheet.Range(2,column+1,Math.Max(1001,rows.Count+1),column+1).Style.NumberFormat.Format = isNumeric ? definition.Key == nameof(ProductMasterImportRowInput.DefaultPcsPerPackage) ? "0" : "0.######" : "@";
                for (var index = 0; index < rows.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var value = ExportValue(rows[index], definition.Key);
                    var cell = sheet.Cell(index+2,column+1);
                    if (value is string text) cell.Value = text;
                    else if (value is int integer) cell.Value = integer;
                    else if (value is decimal measure) cell.Value = (double)measure;
                }
            }
            sheet.Range(1,1,Math.Max(rows.Count+1,2),Columns.Count).Style.Alignment.WrapText = true;
            sheet.SheetView.FreezeRows(1);
            sheet.Range(1,1,Math.Max(rows.Count+1,1),Columns.Count).SetAutoFilter();
            using var content = new MemoryStream();
            book.SaveAs(content, new SaveOptions { EvaluateFormulasBeforeSaving = false });
            cancellationToken.ThrowIfCancellationRequested();
            await AtomicTemplateFileWriter.WriteAsync(content,full,target.Overwrite,null,cancellationToken,publishCommit).ConfigureAwait(false);
        }, cancellationToken);
    }
    /// <summary>引用导出为业务文本，其余字段保持真实类型。</summary>
    private static object? ExportValue(ProductMasterExportRowDto row, string key) => key switch
    {
        nameof(ProductMasterImportRowInput.CategoryPath) => row.CategoryPath,
        nameof(ProductMasterImportRowInput.DefaultSupplierCode) => row.DefaultSupplierCode,
        _ => typeof(ProductMasterTransferData).GetProperty(key, BindingFlags.Public | BindingFlags.Instance)!.GetValue(row.Data)
    };
    /// <summary>仅接受标准 XLSX；底层继续校验真实容器、宏及容量。</summary>
    private static FileStream Open(string path)
    {
        if (!string.Equals(System.IO.Path.GetExtension(path),".xlsx",StringComparison.OrdinalIgnoreCase)) throw new FileOperationException("UnsupportedImportFormat","父级导入仅支持 XLSX。");
        return new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,FileOptions.Asynchronous);
    }
}
