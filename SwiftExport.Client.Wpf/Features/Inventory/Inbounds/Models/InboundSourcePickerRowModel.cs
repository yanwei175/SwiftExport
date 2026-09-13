using CommunityToolkit.Mvvm.ComponentModel;

namespace SwiftExportClient.Features.Inventory.Inbounds.Models;

/// <summary>选源组件的只读采购来源快照；只有勾选状态可编辑，不承担收货或库存计算。</summary>
public sealed partial class InboundSourcePickerRowModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    public Guid PurchaseOrderId { get; init; }
    public Guid PurchaseOrderLineId { get; init; }
    public int SourceVersion { get; init; }
    public Guid ProductId { get; init; }
    public Guid WarehouseSiteId { get; init; }
    public string SourceNo { get; init; } = string.Empty;
    public int LineNumber { get; init; }
    public string BoNo { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public string LinkSku { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? ImagePath { get; init; }
    public string Unit { get; init; } = string.Empty;
    public string SupplierName { get; init; } = string.Empty;
    public string WarehouseName { get; init; } = string.Empty;
    public decimal OriginalQty { get; init; }

    /// <summary>由来源查询提供的正式剩余可收量；不是本次实收输入，也不在客户端扣减草稿。</summary>
    public decimal RemainingReceivableQty { get; init; }
}
