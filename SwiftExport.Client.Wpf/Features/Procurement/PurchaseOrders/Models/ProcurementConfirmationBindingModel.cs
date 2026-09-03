using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Inventory;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>一个客供件完整 BOM 路径的确认绑定；仓库选择与收货目标明确分离。</summary>
public sealed partial class ProcurementConfirmationBindingModel : ObservableObject
{
    public Guid PurchaseOrderLineId { get; set; }
    public string Path { get; set; } = string.Empty;
    public string ComponentSku { get; set; } = string.Empty;
    [ObservableProperty] public partial WarehouseCandidateDto? Warehouse { get; set; }
}
