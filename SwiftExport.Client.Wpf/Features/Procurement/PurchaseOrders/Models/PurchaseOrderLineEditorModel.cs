using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

public sealed partial class PurchaseOrderLineEditorModel : ObservableObject
{
    public Guid Uid { get; init; }
    public int Version { get; init; }
    public int LineNumber { get; init; }
    public string Sku { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal LinkedQty { get; init; }
    public string DeliverySupplierName { get; init; } = string.Empty;
    public IReadOnlyList<PurchaseOrderSourceContributionDto> Contributions { get; init; } = [];
    public IReadOnlyList<SupplierProgressReportDto> ProgressHistory { get; init; } = [];
    public SupplierProgressReportDto? LatestProgress => ProgressHistory.FirstOrDefault();

    [ObservableProperty] public partial DateTime? PlannedDeliveryDate { get; set; }
    [ObservableProperty] public partial string SupplierSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string DescriptionZh { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal? UnitPrice { get; set; }
    [ObservableProperty] public partial decimal? TaxPrice { get; set; }
    [ObservableProperty] public partial string SubNote { get; set; } = string.Empty;

    public static PurchaseOrderLineEditorModel FromDto(PurchaseOrderLineDetailDto dto) => new()
    {
        Uid = dto.Uid, Version = dto.Version, LineNumber = dto.LineNumber, Sku = dto.Sku ?? string.Empty,
        Quantity = dto.Quantity, LinkedQty = dto.LinkedQty, DeliverySupplierName = dto.DeliverySupplierName ?? string.Empty,
        PlannedDeliveryDate = dto.PlannedDeliveryDate, SupplierSku = dto.SupplierSku ?? string.Empty,
        DescriptionZh = dto.DescriptionZh ?? string.Empty, UnitPrice = dto.UnitPrice, TaxPrice = dto.TaxPrice,
        SubNote = dto.SubNote ?? string.Empty, Contributions = dto.Contributions, ProgressHistory = dto.ProgressHistory,
    };
}
