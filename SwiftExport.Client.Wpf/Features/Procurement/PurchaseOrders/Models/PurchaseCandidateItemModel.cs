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
    private bool _synchronizingPurchaseSupplier;
    private bool _synchronizingDeliverySupplier;

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

    /// <summary>采购员最终选择的实际采购供应商稳定身份；名称存在不能替代本字段。</summary>
    [ObservableProperty]
    public partial Guid SupplierId { get; set; }

    /// <summary>实际采购供应商编码快照，与 Uid、名称作为一个逻辑单元参与填充和撤销。</summary>
    [ObservableProperty]
    public partial string PurchaseSupplierCode { get; set; } = string.Empty;

    /// <summary>实际采购供应商名称快照，与 Uid、编码作为一个逻辑单元参与填充和撤销。</summary>
    [ObservableProperty]
    public partial string PurchaseSupplierName { get; set; } = string.Empty;

    /// <summary>客供件本次实际送达供应商稳定身份。</summary>
    [ObservableProperty]
    public partial Guid? DeliveryTargetSupplierId { get; set; }

    /// <summary>实际送达供应商编码快照。</summary>
    [ObservableProperty]
    public partial string DeliverySupplierCode { get; set; } = string.Empty;

    /// <summary>实际送达供应商名称快照。</summary>
    [ObservableProperty]
    public partial string DeliverySupplierName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ProcurementSupplierCandidateDto? PurchaseSupplierSelection { get; set; }

    [ObservableProperty]
    public partial string PurchaseSupplierSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ProcurementSupplierCandidateDto? DeliverySupplierSelection { get; set; }

    [ObservableProperty]
    public partial string DeliverySupplierSearchText { get; set; } = string.Empty;

    /// <summary>当前采购准备是否允许修改行输入；校验和页面写入期间由父 ViewModel 统一冻结。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditPurchaseSupplier))]
    [NotifyPropertyChangedFor(nameof(CanEditDeliverySupplier))]
    public partial bool IsInputEnabled { get; set; } = true;

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
    /// <summary>实际采购方是否允许由 Lookup 编辑或由 EnterpriseGrid 向下填充。</summary>
    public bool CanEditPurchaseSupplier => IsInputEnabled;
    /// <summary>实际送达方仅在输入开放且当前行为客供件时允许编辑或填充。</summary>
    public bool CanEditDeliverySupplier => IsInputEnabled && IsCustomerSupplied;
    public string PurchaseSupplierDisplay => FormatSupplier(SupplierId, PurchaseSupplierCode, PurchaseSupplierName, "未选择");
    public string DeliverySupplierDisplay => IsCustomerSupplied
        ? FormatSupplier(DeliveryTargetSupplierId, DeliverySupplierCode, DeliverySupplierName, "未选择")
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
    partial void OnPurchaseSupplierSelectionChanged(ProcurementSupplierCandidateDto? value)
    {
        if (!_synchronizingPurchaseSupplier) ApplyPurchaseSupplier(value, true);
    }
    partial void OnDeliverySupplierSelectionChanged(ProcurementSupplierCandidateDto? value)
    {
        if (!_synchronizingDeliverySupplier) ApplyDeliverySupplier(value, true);
    }
    partial void OnSupplierIdChanged(Guid value) => SynchronizePurchaseSupplierFromFacts();
    partial void OnPurchaseSupplierCodeChanged(string value) => SynchronizePurchaseSupplierFromFacts();
    partial void OnPurchaseSupplierNameChanged(string value) => SynchronizePurchaseSupplierFromFacts();
    partial void OnDeliveryTargetSupplierIdChanged(Guid? value) => SynchronizeDeliverySupplierFromFacts();
    partial void OnDeliverySupplierCodeChanged(string value) => SynchronizeDeliverySupplierFromFacts();
    partial void OnDeliverySupplierNameChanged(string value) => SynchronizeDeliverySupplierFromFacts();
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
        var changed = SupplierId != (supplier?.Uid ?? Guid.Empty)
            || !string.Equals(PurchaseSupplierCode, supplier?.SupplierCode ?? string.Empty, StringComparison.Ordinal)
            || !string.Equals(PurchaseSupplierName, supplier?.SupplierName ?? string.Empty, StringComparison.Ordinal);
        _synchronizingPurchaseSupplier = true;
        try
        {
            PurchaseSupplierSelection = supplier;
            SupplierId = supplier?.Uid ?? Guid.Empty;
            PurchaseSupplierCode = supplier?.SupplierCode ?? string.Empty;
            PurchaseSupplierName = supplier?.SupplierName ?? string.Empty;
            PurchaseSupplierSearchText = FormatSupplier(
                supplier?.Uid, supplier?.SupplierCode, supplier?.SupplierName, string.Empty);
        }
        finally
        {
            _synchronizingPurchaseSupplier = false;
        }
        OnPropertyChanged(nameof(PurchaseSupplierDisplay));
        if (notify && changed) ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyDeliverySupplier(ProcurementSupplierCandidateDto? supplier, bool notify)
    {
        var changed = DeliveryTargetSupplierId != supplier?.Uid
            || !string.Equals(DeliverySupplierCode, supplier?.SupplierCode ?? string.Empty, StringComparison.Ordinal)
            || !string.Equals(DeliverySupplierName, supplier?.SupplierName ?? string.Empty, StringComparison.Ordinal);
        _synchronizingDeliverySupplier = true;
        try
        {
            DeliverySupplierSelection = supplier;
            DeliveryTargetSupplierId = supplier?.Uid;
            DeliverySupplierCode = supplier?.SupplierCode ?? string.Empty;
            DeliverySupplierName = supplier?.SupplierName ?? string.Empty;
            DeliverySupplierSearchText = FormatSupplier(
                supplier?.Uid, supplier?.SupplierCode, supplier?.SupplierName, string.Empty);
        }
        finally
        {
            _synchronizingDeliverySupplier = false;
        }
        OnPropertyChanged(nameof(DeliverySupplierDisplay));
        if (notify && changed) ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>复合填充或撤销直接写事实字段后，重建 Lookup 选择对象并使请求读取同一组值。</summary>
    private void SynchronizePurchaseSupplierFromFacts()
    {
        if (_synchronizingPurchaseSupplier) return;
        _synchronizingPurchaseSupplier = true;
        try
        {
            PurchaseSupplierSelection = SupplierId == Guid.Empty
                ? null
                : new ProcurementSupplierCandidateDto
                {
                    Uid = SupplierId,
                    SupplierCode = PurchaseSupplierCode,
                    SupplierName = PurchaseSupplierName,
                };
            PurchaseSupplierSearchText = FormatSupplier(
                SupplierId == Guid.Empty ? null : SupplierId,
                PurchaseSupplierCode,
                PurchaseSupplierName,
                string.Empty);
        }
        finally
        {
            _synchronizingPurchaseSupplier = false;
        }
        OnPropertyChanged(nameof(PurchaseSupplierDisplay));
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>复合填充或撤销直接写送达事实后，重建 Lookup 选择对象。</summary>
    private void SynchronizeDeliverySupplierFromFacts()
    {
        if (_synchronizingDeliverySupplier) return;
        _synchronizingDeliverySupplier = true;
        try
        {
            DeliverySupplierSelection = !DeliveryTargetSupplierId.HasValue
                ? null
                : new ProcurementSupplierCandidateDto
                {
                    Uid = DeliveryTargetSupplierId.Value,
                    SupplierCode = DeliverySupplierCode,
                    SupplierName = DeliverySupplierName,
                };
            DeliverySupplierSearchText = FormatSupplier(
                DeliveryTargetSupplierId,
                DeliverySupplierCode,
                DeliverySupplierName,
                string.Empty);
        }
        finally
        {
            _synchronizingDeliverySupplier = false;
        }
        OnPropertyChanged(nameof(DeliverySupplierDisplay));
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string FormatSupplier(Guid? supplierId, string? code, string? name, string fallback)
    {
        if (!supplierId.HasValue || supplierId.Value == Guid.Empty) return fallback;
        var value = $"{code} {name}".Trim();
        return string.IsNullOrWhiteSpace(value) ? supplierId.Value.ToString() : value;
    }

    /// <summary>从只读候选契约建立采购页面模型。</summary>
    public static PurchaseCandidateItemModel FromDto(PurchaseDraftCandidateDto dto)
    {
        var row = dto.Adapt<PurchaseCandidateItemModel>();
        row.InitializeInputs(dto);
        return row;
    }
}
