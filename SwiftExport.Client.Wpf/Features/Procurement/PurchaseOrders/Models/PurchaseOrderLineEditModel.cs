using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>采购详情统一行编辑模型；负责行状态、白名单编辑、创建输入和本地校验。</summary>
public sealed partial class PurchaseOrderLineEditModel : BaseLineModel<PurchaseOrderLineDetailDto>
{
    [ObservableProperty] public partial int LineNumber { get; set; }
    [ObservableProperty] public partial Guid ProductId { get; set; }
    [ObservableProperty] public partial ProcurementProductCandidateDto? Product { get; set; }
    [ObservableProperty] public partial string MainImage { get; set; } = string.Empty;
    [ObservableProperty] public partial string Sku { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string LinkSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplierSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string DescriptionZh { get; set; } = string.Empty;
    [ObservableProperty] public partial string FinishZh { get; set; } = string.Empty;
    [ObservableProperty] public partial string FeaturesZh { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequiresDeliverySupplier))]
    public partial string MaterialType { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplyType { get; set; } = string.Empty;
    [ObservableProperty] public partial string UnitSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal Quantity { get; set; }
    [ObservableProperty] public partial decimal? UnitPrice { get; set; }
    [ObservableProperty] public partial decimal? TaxPrice { get; set; }
    [ObservableProperty] public partial decimal? UnitAmount { get; set; }
    [ObservableProperty] public partial decimal? TaxAmount { get; set; }
    [ObservableProperty] public partial DateTime? PlannedDeliveryDate { get; set; }
    [ObservableProperty] public partial ProcurementSupplierCandidateDto? DeliverySupplier { get; set; }
    [ObservableProperty] public partial Guid? DeliverySupplierId { get; set; }
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
    /// <summary>仅客供件需要独立送达供应商；该目标与正式收货仓库互不推导。</summary>
    public bool RequiresDeliverySupplier => MaterialType == MaterialTypesEnum.客供件;

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
        if (RequiresDeliverySupplier && (!DeliverySupplierId.HasValue || DeliverySupplierId == Guid.Empty)) return "客供件必须选择送达供应商。";
        if (!UnitPrice.HasValue) return "未税单价不能为空；无报价时请明确输入 0。";
        if (UnitPrice < 0) return "未税单价不能为负数。";
        return null;
    }

    partial void OnProductChanged(ProcurementProductCandidateDto? value)
    {
        if (!IsNew) return;
        if (value is null) { ClearProductFields(); return; }
        ApplyProductFields(value);
    }

    /// <summary>将采购产品候选完整应用到新增行；已保存行的产品身份不可通过普通草稿编辑改变。</summary>
    public void ApplyProduct(ProcurementProductCandidateDto product)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (!IsNew) return;
        if (ReferenceEquals(Product, product)) ApplyProductFields(product);
        else Product = product;
    }

    /// <summary>集中回填候选中的当前主数据和供应商关系值，最终订单快照仍由后端重新读取。</summary>
    private void ApplyProductFields(ProcurementProductCandidateDto value)
    {
        ProductId = value.Uid;
        MainImage = value.MainImage;
        Sku = value.Sku;
        CustomerSku = value.CustomerSku;
        LinkSku = value.LinkSku;
        SupplierSku = value.SupplierSku;
        DescriptionZh = value.DescriptionZh;
        FinishZh = value.FinishZh;
        FeaturesZh = value.FeaturesZh;
        MaterialType = value.MaterialType;
        SupplyType = value.SupplyType;
        UnitSnapshot = value.Unit;
        UnitPrice = value.UnitPrice;
        TaxPrice = value.TaxPrice;
        if (!RequiresDeliverySupplier) ClearDeliverySupplier();
    }

    partial void OnDeliverySupplierChanged(ProcurementSupplierCandidateDto? value)
    {
        if (!IsNew) return;
        DeliverySupplierId = value?.Uid;
        DeliverySupplierCode = value?.SupplierCode ?? string.Empty;
        DeliverySupplierName = value?.SupplierName ?? string.Empty;
    }

    /// <summary>清除创建行产品及其关系快照；客户或供应商主体改变时必须重新选择。</summary>
    public void ClearProductSelection()
    {
        if (!IsNew) return;
        Product = null;
        ClearProductFields();
    }

    private void ClearProductFields()
    {
        ProductId = Guid.Empty; MainImage = Sku = CustomerSku = LinkSku = SupplierSku = DescriptionZh = string.Empty;
        FinishZh = FeaturesZh = MaterialType = SupplyType = UnitSnapshot = string.Empty;
        ClearDeliverySupplier();
    }

    /// <summary>清除创建行客供件送达目标；非客供件不得保留先前选择。</summary>
    public void ClearDeliverySupplier()
    {
        if (!IsNew) return;
        DeliverySupplier = null; DeliverySupplierId = null;
        DeliverySupplierCode = DeliverySupplierName = string.Empty;
    }
}
