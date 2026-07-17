using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

public sealed partial class PurchaseOrderLineEditorModel : ObservableObject
{
    public Guid Uid { get; init; }
    public int Version { get; init; }
    public int LineNumber { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string CustomerSku { get; init; } = string.Empty;
    public string LinkSku { get; init; } = string.Empty;
    public string FinishZh { get; init; } = string.Empty;
    public string FeaturesZh { get; init; } = string.Empty;
    public string MaterialType { get; init; } = string.Empty;
    public string SupplyType { get; init; } = string.Empty;
    public string MainImage { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal RecQty { get; init; }
    public decimal? UnitAmount { get; init; }
    public decimal? TaxAmount { get; init; }
    public string UnreceivedQtyText { get; init; } = "尚未接入";
    public decimal LinkedQty { get; init; }
    public string DeliverySupplierName { get; init; } = string.Empty;
    public IReadOnlyList<PurchaseOrderSourceContributionDto> Contributions { get; init; } = [];
    public IReadOnlyList<SupplierProgressReportDto> ProgressHistory { get; init; } = [];
    public SupplierProgressReportDto? LatestProgress => ProgressHistory.FirstOrDefault();
    public decimal LatestReadyQty => LatestProgress?.ReadyQty ?? 0m;
    public DateTime? LatestExpectedReadyDate => LatestProgress?.ExpectedReadyDate;
    public string SourceSummary => Contributions.Count == 0
        ? string.Empty
        : string.Join("、", Contributions.Select(x => $"{x.SourceNo}/{x.SourceLineNumber}"));

    [ObservableProperty] public partial DateTime? PlannedDeliveryDate { get; set; }
    [ObservableProperty] public partial string SupplierSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string DescriptionZh { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal? UnitPrice { get; set; }
    [ObservableProperty] public partial decimal? TaxPrice { get; set; }
    [ObservableProperty] public partial string SubNote { get; set; } = string.Empty;

    public static PurchaseOrderLineEditorModel FromDto(PurchaseOrderLineDetailDto dto) => new()
    {
        Uid = dto.Uid, Version = dto.Version, LineNumber = dto.LineNumber, Sku = dto.Sku ?? string.Empty,
        CustomerSku = dto.CustomerSku ?? string.Empty, LinkSku = dto.LinkSku ?? string.Empty,
        FinishZh = dto.FinishZh ?? string.Empty, FeaturesZh = dto.FeaturesZh ?? string.Empty,
        MaterialType = dto.MaterialType ?? string.Empty, SupplyType = dto.SupplyType ?? string.Empty,
        MainImage = dto.MainImage ?? string.Empty, Quantity = dto.Quantity, RecQty = dto.RecQty,
        UnitAmount = dto.UnitAmount, TaxAmount = dto.TaxAmount, UnreceivedQtyText = dto.UnreceivedQtyText ?? "尚未接入",
        LinkedQty = dto.LinkedQty, DeliverySupplierName = dto.DeliverySupplierName ?? string.Empty,
        PlannedDeliveryDate = dto.PlannedDeliveryDate, SupplierSku = dto.SupplierSku ?? string.Empty,
        DescriptionZh = dto.DescriptionZh ?? string.Empty, UnitPrice = dto.UnitPrice, TaxPrice = dto.TaxPrice,
        SubNote = dto.SubNote ?? string.Empty, Contributions = dto.Contributions, ProgressHistory = dto.ProgressHistory,
    };
}
