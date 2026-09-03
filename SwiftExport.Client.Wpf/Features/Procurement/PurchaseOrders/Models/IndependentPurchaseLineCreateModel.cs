using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>独立采购创建行；仅承载创建命令允许的输入和当前候选选择。</summary>
public sealed partial class IndependentPurchaseLineCreateModel : ObservableObject
{
    [ObservableProperty] public partial ProcurementProductCandidateDto? Product { get; set; }
    [ObservableProperty] public partial WarehouseCandidateDto? Warehouse { get; set; }
    [ObservableProperty] public partial decimal Quantity { get; set; } = 1;
    [ObservableProperty] public partial decimal? UnitPrice { get; set; }
    [ObservableProperty] public partial decimal? TaxPrice { get; set; }
    [ObservableProperty] public partial DateTime? PlannedDeliveryDate { get; set; }
    [ObservableProperty] public partial string SupplierSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string DescriptionZh { get; set; } = string.Empty;
    [ObservableProperty] public partial string SubNote { get; set; } = string.Empty;
}
