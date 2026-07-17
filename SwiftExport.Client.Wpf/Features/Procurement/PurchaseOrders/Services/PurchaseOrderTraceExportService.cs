using System.Globalization;
using System.IO;
using Microsoft.Win32;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Wpf.EnterpriseGrid.Exporting;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

public sealed class PurchaseOrderTraceExportService : IPurchaseOrderTraceExportService
{
    private readonly IGridExportProvider _provider = new XlsxGridExportProvider();
    public async Task<string?> ExportAsync(PurchaseOrderTraceDto trace, CancellationToken cancellationToken = default)
    {
        var dialog = new SaveFileDialog { Title = "导出采购来源追溯", Filter = "Excel 工作簿 (*.xlsx)|*.xlsx", DefaultExt = ".xlsx", AddExtension = true, FileName = $"采购追溯_{Safe(trace.OrderNo)}_{DateTime.Now:yyyyMMddHHmm}.xlsx" };
        if (dialog.ShowDialog() != true) return null;
        var columns = new List<GridExportColumn>
        {
            new("order-no","采购单号",null,0,null),new("source-type","来源类型",null,1,null),new("source-no","来源单号",null,2,null),
            new("source-line","来源行",null,3,null),new("sku","SKU",null,4,null),new("qty","有效贡献",null,5,"N4"),
            new("demand-date","原需求交期",null,6,"yyyy-MM-dd"),new("planned-date","计划交期",null,7,"yyyy-MM-dd")
        };
        var rows = trace.Contributions.Select(x => new GridExportRow(new List<GridExportCell>
        {
            Cell(trace.OrderNo),Cell(x.SourceType),Cell(x.SourceNo),Cell(x.SourceLineNumber),Cell(x.Sku),
            Cell(x.EffectiveQty,x.EffectiveQty.ToString("N4",CultureInfo.GetCultureInfo("zh-CN"))),
            Cell(x.DemandRequiredDate,x.DemandRequiredDate?.ToString("yyyy-MM-dd") ?? string.Empty),
            Cell(x.PlannedDeliveryDate,x.PlannedDeliveryDate?.ToString("yyyy-MM-dd") ?? string.Empty)
        })).ToList();
        var request = new GridExportRequest(columns, rows, new GridExportOptions { IncludeHeaders = true, ValueMode = GridExportValueMode.Display, FormulaInjectionPolicy = GridFormulaInjectionPolicy.EscapeText, Culture = CultureInfo.GetCultureInfo("zh-CN"), WorksheetName = "采购来源追溯" });
        await using var stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
        await _provider.ExportAsync(request, stream, cancellationToken: cancellationToken);
        return dialog.FileName;
    }
    private static GridExportCell Cell(object? value, string? display = null) => new(value, display ?? Convert.ToString(value, CultureInfo.GetCultureInfo("zh-CN")) ?? string.Empty);
    private static string Safe(string? value) { var text = string.IsNullOrWhiteSpace(value) ? "未命名" : value.Trim(); foreach (var ch in Path.GetInvalidFileNameChars()) text = text.Replace(ch, '_'); return text; }
}
