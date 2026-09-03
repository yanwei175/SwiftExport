using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>采购详情行编辑模型；只开放新采购商务白名单，产品、数量、单位和收货目标保持只读。</summary>
public sealed partial class ProcurementOrderLineEditModel : ObservableObject
{
    public Guid PurchaseOrderLineId { get; set; }
    public int LineNumber { get; set; }
    public int Version { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string UnitSnapshot { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public Guid WarehouseSiteId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string DeliverySupplierName { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal? UnitPrice { get; set; }
    [ObservableProperty] public partial decimal? TaxPrice { get; set; }
    [ObservableProperty] public partial DateTime? PlannedDeliveryDate { get; set; }
    [ObservableProperty] public partial string SupplierSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string DescriptionZh { get; set; } = string.Empty;
    [ObservableProperty] public partial string SubNote { get; set; } = string.Empty;

    /// <summary>从专用详情契约建立可编辑投影，不携带旧链来源或跟进字段。</summary>
    public static ProcurementOrderLineEditModel FromDto(ProcurementOrderLineDto dto) => new()
    {
        PurchaseOrderLineId = dto.PurchaseOrderLineId, LineNumber = dto.LineNumber, Version = dto.Version,
        ProductId = dto.ProductId, Sku = dto.Sku, UnitSnapshot = dto.UnitSnapshot, Quantity = dto.Quantity,
        WarehouseSiteId = dto.WarehouseSiteId, WarehouseName = dto.WarehouseName,
        DeliverySupplierName = dto.DeliverySupplierName, UnitPrice = dto.UnitPrice, TaxPrice = dto.TaxPrice,
        PlannedDeliveryDate = dto.PlannedDeliveryDate, SupplierSku = dto.SupplierSku ?? string.Empty,
        DescriptionZh = dto.DescriptionZh ?? string.Empty, SubNote = dto.SubNote ?? string.Empty
    };
}
