using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>采购详情统一行编辑模型；负责行状态、白名单编辑、创建输入和本地校验。</summary>
public sealed partial class PurchaseOrderLineEditModel : BaseLineModel<PurchaseOrderLineDetailDto>
{
    [ObservableProperty] public partial int LineNumber { get; set; }
    [ObservableProperty] public partial Guid ProductId { get; set; }
    [ObservableProperty] public partial ProcurementProductCandidateDto? Product { get; set; }
    [ObservableProperty] public partial WarehouseCandidateDto? Warehouse { get; set; }
    [ObservableProperty] public partial string MainImage { get; set; } = string.Empty;
    [ObservableProperty] public partial string Sku { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string LinkSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplierSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string DescriptionZh { get; set; } = string.Empty;
    [ObservableProperty] public partial string FinishZh { get; set; } = string.Empty;
    [ObservableProperty] public partial string FeaturesZh { get; set; } = string.Empty;
    [ObservableProperty] public partial string MaterialType { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplyType { get; set; } = string.Empty;
    [ObservableProperty] public partial string UnitSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal Quantity { get; set; }
    [ObservableProperty] public partial decimal? UnitPrice { get; set; }
    [ObservableProperty] public partial decimal? TaxPrice { get; set; }
    [ObservableProperty] public partial decimal? UnitAmount { get; set; }
    [ObservableProperty] public partial decimal? TaxAmount { get; set; }
    [ObservableProperty] public partial DateTime? PlannedDeliveryDate { get; set; }
    [ObservableProperty] public partial Guid WarehouseSiteId { get; set; }
    [ObservableProperty] public partial string WarehouseCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string WarehouseName { get; set; } = string.Empty;
    [ObservableProperty] public partial Guid DeliverySupplierId { get; set; }
    [ObservableProperty] public partial string DeliverySupplierCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string DeliverySupplierName { get; set; } = string.Empty;
    [ObservableProperty] public partial string SourceType { get; set; } = string.Empty;
    [ObservableProperty] public partial string SourceNo { get; set; } = string.Empty;
    [ObservableProperty] public partial Guid? SourceUid { get; set; }
    [ObservableProperty] public partial Guid? SourceLineId { get; set; }
    [ObservableProperty] public partial int? SourceLineNumber { get; set; }
    [ObservableProperty] public partial string SubNote { get; set; } = string.Empty;

    public string SourceSummary => string.IsNullOrWhiteSpace(SourceNo)
        ? SourceType
        : $"{SourceNo}{(SourceLineNumber.HasValue ? $"/{SourceLineNumber}" : string.Empty)}";

    protected override bool IsTrackingIgnoredProperty(string? propertyName)
        => base.IsTrackingIgnoredProperty(propertyName)
            || propertyName is nameof(MainImage) or nameof(SourceSummary) or nameof(UnitAmount) or nameof(TaxAmount);

    /// <inheritdoc />
    protected override void LoadFromDto(PurchaseOrderLineDetailDto dto)
        => dto.Adapt(this, TypeAdapterConfig.GlobalSettings);

    /// <inheritdoc />
    public override PurchaseOrderLineDetailDto ToDto()
        => this.Adapt<PurchaseOrderLineDetailDto>(TypeAdapterConfig.GlobalSettings);

    /// <inheritdoc />
    public override string? ValidateRow()
    {
        if (IsNew && Product is null) return "必须选择产品。";
        if (Quantity <= 0) return "数量必须大于零。";
        if (string.IsNullOrWhiteSpace(IsNew ? Product?.Unit : UnitSnapshot)) return "必须有产品单位。";
        if ((IsNew && Warehouse is null) || (!IsNew && WarehouseSiteId == Guid.Empty)) return "必须选择合法收货目标。";
        if (UnitPrice < 0) return "未税单价不能为负数。";
        return null;
    }

    partial void OnProductChanged(ProcurementProductCandidateDto? value)
    {
        if (!IsNew || value is null) return;
        ProductId = value.Uid;
        Sku = value.Sku;
        DescriptionZh = value.DescriptionZh;
        UnitSnapshot = value.Unit;
    }

    partial void OnWarehouseChanged(WarehouseCandidateDto? value)
    {
        if (!IsNew || value is null) return;
        WarehouseSiteId = value.Uid;
        WarehouseCode = value.Code ?? string.Empty;
        WarehouseName = value.Name ?? string.Empty;
    }
}
