using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
namespace SwiftExportClient.Features.OrderTracking.PurchaseOrders.Models;

/// <summary>完整追加反馈与对应采购行号；无法读取行事实时仍保留反馈稳定身份。</summary>
public sealed partial class PurchaseOrderReportRowModel(SupplierProgressReportDto data, int? lineNumber)
{
    /// <summary>后端保存的原反馈快照，不提供写命令。</summary>
    public SupplierProgressReportDto Data { get; } = data;
    /// <summary>按稳定采购行身份匹配行号，不按型号猜测。</summary>
    public int? LineNumber { get; } = lineNumber;
}
