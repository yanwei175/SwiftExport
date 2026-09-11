using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using System.Collections;
using System.ComponentModel;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>采购从物料需求取数时的页面候选模型，独立持有选择状态和采购展示字段。</summary>
public sealed partial class PurchaseCandidateItemModel : ObservableObject, INotifyDataErrorInfo
{
    /// <summary>物料需求 Uid。</summary>
    public Guid Uid { get; init; }
    /// <summary>物料需求并发版本。</summary>
    public int Version { get; init; }
    /// <summary>来源需求归属客户稳定身份。</summary>
    public Guid? CustomerId { get; init; }
    public string CustomerCode { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    /// <summary>来源业务类型。</summary>
    public string SourceType { get; init; } = string.Empty;
    /// <summary>来源业务 Uid。</summary>
    public Guid SourceUid { get; init; }
    /// <summary>来源业务单号。</summary>
    public string SourceNo { get; init; } = string.Empty;
    /// <summary>来源业务行号。</summary>
    public int? SourceLineNumber { get; init; }
    /// <summary>物料 SKU 快照。</summary>
    public string Sku { get; init; } = string.Empty;
    /// <summary>是否为送往父件实际执行供应商的客供件。</summary>
    public bool IsCustomerSupplied { get; init; }
    /// <summary>产品关联 SKU。</summary>
    public string LinkSku { get; init; } = string.Empty;
    /// <summary>物料描述。</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>产品图片路径。</summary>
    public string ImagePath { get; init; } = string.Empty;
    /// <summary>推荐采购供应商 Uid。</summary>
    public Guid? SuggestedPurchaseSupplierId { get; init; }
    /// <summary>推荐采购供应商编码。</summary>
    public string SuggestedPurchaseSupplierCode { get; init; } = string.Empty;
    /// <summary>推荐采购供应商名称。</summary>
    public string SuggestedPurchaseSupplierName { get; init; } = string.Empty;
    /// <summary>建议送达供应商 Uid。</summary>
    public Guid? SuggestedDeliverySupplierId { get; init; }
    /// <summary>建议送达供应商编码。</summary>
    public string SuggestedDeliverySupplierCode { get; init; } = string.Empty;
    /// <summary>建议送达供应商名称。</summary>
    public string SuggestedDeliverySupplierName { get; init; } = string.Empty;
    /// <summary>建议送达方无法确定时的事实提示。</summary>
    public string SuggestedDeliverySupplierHint { get; init; } = string.Empty;
    /// <summary>客供件父件活动采购单中的实际采购供应商事实。</summary>
    public List<PurchaseDraftActualSupplierFactDto> ParentActualPurchaseSuppliers { get; init; } = [];
    /// <summary>来源需求交期。</summary>
    public DateTime? DemandRequiredDate { get; init; }
    /// <summary>当前尚未被有效采购贡献承接的数量。</summary>
    public decimal RemainingQty { get; init; }

    public string SuggestedSupplierSku { get; init; } = string.Empty;

    public Guid SupplierId { get; private set; }
    public Guid? DeliveryTargetSupplierId { get; private set; }

    [ObservableProperty]
    public partial ProcurementSupplierCandidateDto? PurchaseSupplierSelection { get; set; }

    [ObservableProperty]
    public partial string PurchaseSupplierSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ProcurementSupplierCandidateDto? DeliverySupplierSelection { get; set; }

    [ObservableProperty]
    public partial string DeliverySupplierSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal PurchaseQty { get; set; }

    [ObservableProperty]
    public partial decimal UnitPrice { get; set; }

    [ObservableProperty]
    public partial decimal TaxPrice { get; set; }

    [ObservableProperty]
    public partial DateTime? PlannedDeliveryDate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    public partial string ValidationMessage { get; set; } = string.Empty;

    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationMessage);
    public bool HasErrors => HasValidationError;
    public string PurchaseSupplierDisplay => FormatSupplier(PurchaseSupplierSelection, "未选择");
    public string DeliverySupplierDisplay => IsCustomerSupplied
        ? FormatSupplier(DeliverySupplierSelection, "未选择")
        : "不适用";

    public event EventHandler? ValueChanged;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    partial void OnIsCheckedChanged(bool value) => ValueChanged?.Invoke(this, EventArgs.Empty);
    partial void OnPurchaseQtyChanged(decimal value) => ValueChanged?.Invoke(this, EventArgs.Empty);
    partial void OnUnitPriceChanged(decimal value) => ValueChanged?.Invoke(this, EventArgs.Empty);
    partial void OnTaxPriceChanged(decimal value) => ValueChanged?.Invoke(this, EventArgs.Empty);
    partial void OnPlannedDeliveryDateChanged(DateTime? value) => ValueChanged?.Invoke(this, EventArgs.Empty);
    partial void OnValidationMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasErrors));
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(string.Empty));
    }

    /// <summary>向 EnterpriseGrid 暴露服务端行级阻断，不把价格零值或空交期误判为错误。</summary>
    public IEnumerable GetErrors(string? propertyName) => HasValidationError
        ? new[] { ValidationMessage }
        : Array.Empty<string>();

    /// <summary>推荐采购方与推荐送达方分别初始化实际选择，不读取父件历史采购方覆盖建议。</summary>
    public void InitializeInputs(PurchaseDraftCandidateDto dto)
    {
        PurchaseQty = dto.RemainingQty;
        UnitPrice = dto.SuggestedUnitPrice ?? 0m;
        TaxPrice = dto.SuggestedTaxPrice ?? 0m;
        PlannedDeliveryDate = dto.DemandRequiredDate;
        ApplyPurchaseSupplier(dto.SuggestedPurchaseSupplierId.HasValue
            ? new ProcurementSupplierCandidateDto
            {
                Uid = dto.SuggestedPurchaseSupplierId.Value,
                SupplierCode = dto.SuggestedPurchaseSupplierCode,
                SupplierName = dto.SuggestedPurchaseSupplierName,
            }
            : null, false);
        ApplyDeliverySupplier(dto.IsCustomerSupplied && dto.SuggestedDeliverySupplierId.HasValue
            ? new ProcurementSupplierCandidateDto
            {
                Uid = dto.SuggestedDeliverySupplierId.Value,
                SupplierCode = dto.SuggestedDeliverySupplierCode,
                SupplierName = dto.SuggestedDeliverySupplierName,
            }
            : null, false);
    }

    public void CommitPurchaseSupplier() => ApplyPurchaseSupplier(PurchaseSupplierSelection, true);
    public void ClearPurchaseSupplier() => ApplyPurchaseSupplier(null, true);
    public void CommitDeliverySupplier() => ApplyDeliverySupplier(DeliverySupplierSelection, true);
    public void ClearDeliverySupplier() => ApplyDeliverySupplier(null, true);

    private void ApplyPurchaseSupplier(ProcurementSupplierCandidateDto? supplier, bool notify)
    {
        var changed = SupplierId != (supplier?.Uid ?? Guid.Empty);
        PurchaseSupplierSelection = supplier;
        SupplierId = supplier?.Uid ?? Guid.Empty;
        PurchaseSupplierSearchText = FormatSupplier(supplier, string.Empty);
        OnPropertyChanged(nameof(PurchaseSupplierDisplay));
        if (notify && changed) ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyDeliverySupplier(ProcurementSupplierCandidateDto? supplier, bool notify)
    {
        var changed = DeliveryTargetSupplierId != supplier?.Uid;
        DeliverySupplierSelection = supplier;
        DeliveryTargetSupplierId = supplier?.Uid;
        DeliverySupplierSearchText = FormatSupplier(supplier, string.Empty);
        OnPropertyChanged(nameof(DeliverySupplierDisplay));
        if (notify && changed) ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string FormatSupplier(ProcurementSupplierCandidateDto? supplier, string fallback)
    {
        if (supplier is null) return fallback;
        var value = $"{supplier.SupplierCode} {supplier.SupplierName}".Trim();
        return string.IsNullOrWhiteSpace(value) ? supplier.Uid.ToString() : value;
    }

    /// <summary>从只读候选契约建立采购页面模型。</summary>
    public static PurchaseCandidateItemModel FromDto(PurchaseDraftCandidateDto dto)
    {
        var row = dto.Adapt<PurchaseCandidateItemModel>();
        row.InitializeInputs(dto);
        return row;
    }
}
