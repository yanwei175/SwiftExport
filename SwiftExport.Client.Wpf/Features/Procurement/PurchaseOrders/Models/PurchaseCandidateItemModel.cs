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
    /// <summary>客供件明确送往的父物料需求 Uid；仅用于同一已加载来源内识别真实父子关系。</summary>
    public Guid? DeliveryTargetNetDemandId { get; init; }
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
    public partial Guid? DeliveryTargetSupplierId { get; private set; }

    /// <summary>实际送达供应商编码快照。</summary>
    [ObservableProperty]
    public partial string DeliverySupplierCode { get; private set; } = string.Empty;

    /// <summary>实际送达供应商名称快照。</summary>
    [ObservableProperty]
    public partial string DeliverySupplierName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial ProcurementSupplierCandidateDto? PurchaseSupplierSelection { get; set; }

    [ObservableProperty]
    public partial string PurchaseSupplierSearchText { get; set; } = string.Empty;

    /// <summary>当前采购准备是否允许修改行输入；校验和页面写入期间由父 ViewModel 统一冻结。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditPurchaseSupplier))]
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
    [NotifyPropertyChangedFor(nameof(ValidationDisplayMessage))]
    public partial string ValidationMessage { get; set; } = string.Empty;

    /// <summary>候选恢复产生的本地阻断；为空表示仍处于服务端待采购范围。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    [NotifyPropertyChangedFor(nameof(ValidationDisplayMessage))]
    [NotifyPropertyChangedFor(nameof(CanEditPurchaseSupplier))]
    public partial string RecoveryValidationMessage { get; private set; } = string.Empty;

    /// <summary>当前行是否仍属于服务端待采购范围；缺失行只允许取消勾选。</summary>
    public bool IsAvailableForPurchase => string.IsNullOrWhiteSpace(RecoveryValidationMessage);
    private string QuantityValidationMessage => PurchaseQty <= 0
        ? "采购数量必须大于 0。"
        : PurchaseQty > RemainingQty
            ? $"采购数量超过当前待采数量 {RemainingQty:N4}，请修改后重新校验。"
            : string.Empty;
    /// <summary>合并恢复状态、数量边界和服务端阻断后的最终行提示。</summary>
    public string ValidationDisplayMessage => string.Join("；", new[]
        {
            RecoveryValidationMessage,
            QuantityValidationMessage,
            ValidationMessage,
        }.Where(static message => !string.IsNullOrWhiteSpace(message)));
    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationDisplayMessage);
    public bool HasErrors => HasValidationError;
    /// <summary>实际采购方是否允许由 Lookup 编辑或由 EnterpriseGrid 向下填充。</summary>
    public bool CanEditPurchaseSupplier => IsInputEnabled && IsAvailableForPurchase;
    public string PurchaseSupplierDisplay => FormatSupplier(SupplierId, PurchaseSupplierCode, PurchaseSupplierName, "未选择");
    public string DeliverySupplierDisplay => IsCustomerSupplied
        ? FormatSupplier(DeliveryTargetSupplierId, DeliverySupplierCode, DeliverySupplierName, "未选择")
        : "不适用";

    public event EventHandler? ValueChanged;
    /// <summary>勾选状态变化通知；与采购输入变化分离，供页面区分单行操作和批量选择。</summary>
    public event EventHandler? SelectionChanged;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    partial void OnIsCheckedChanged(bool value) => SelectionChanged?.Invoke(this, EventArgs.Empty);
    partial void OnPurchaseQtyChanged(decimal value)
    {
        OnPropertyChanged(nameof(ValidationDisplayMessage));
        OnPropertyChanged(nameof(HasValidationError));
        OnPropertyChanged(nameof(HasErrors));
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(PurchaseQty)));
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
    partial void OnUnitPriceChanged(decimal value) => ValueChanged?.Invoke(this, EventArgs.Empty);
    partial void OnTaxPriceChanged(decimal value) => ValueChanged?.Invoke(this, EventArgs.Empty);
    partial void OnPlannedDeliveryDateChanged(DateTime? value) => ValueChanged?.Invoke(this, EventArgs.Empty);
    partial void OnPurchaseSupplierSelectionChanged(ProcurementSupplierCandidateDto? value)
    {
        if (!_synchronizingPurchaseSupplier) ApplyPurchaseSupplier(value, true);
    }
    partial void OnSupplierIdChanged(Guid value) => SynchronizePurchaseSupplierFromFacts();
    partial void OnPurchaseSupplierCodeChanged(string value) => SynchronizePurchaseSupplierFromFacts();
    partial void OnPurchaseSupplierNameChanged(string value) => SynchronizePurchaseSupplierFromFacts();
    partial void OnValidationMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasErrors));
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(string.Empty));
    }
    partial void OnRecoveryValidationMessageChanged(string value)
    {
        OnPropertyChanged(nameof(IsAvailableForPurchase));
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(string.Empty));
    }

    /// <summary>向 EnterpriseGrid 暴露行级业务阻断；属性级仅保留 WPF 转换错误，避免业务错误绘制整行单元格红框。</summary>
    public IEnumerable GetErrors(string? propertyName) => HasValidationError && string.IsNullOrWhiteSpace(propertyName)
        ? new[] { ValidationDisplayMessage }
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
        ApplyDeliverySupplier(
            dto.IsCustomerSupplied ? dto.SuggestedDeliverySupplierId : null,
            dto.IsCustomerSupplied ? dto.SuggestedDeliverySupplierCode : string.Empty,
            dto.IsCustomerSupplied ? dto.SuggestedDeliverySupplierName : string.Empty);
    }

    /// <summary>以最新服务端事实重建候选，同时保留采购员已经输入的业务值，不重新应用推荐值。</summary>
    public static PurchaseCandidateItemModel MergeRefreshed(
        PurchaseDraftCandidateDto dto,
        PurchaseCandidateItemModel existing)
    {
        var refreshed = dto.Adapt<PurchaseCandidateItemModel>();
        refreshed.IsChecked = existing.IsChecked;
        refreshed.PurchaseQty = existing.PurchaseQty;
        refreshed.UnitPrice = existing.UnitPrice;
        refreshed.TaxPrice = existing.TaxPrice;
        refreshed.PlannedDeliveryDate = existing.PlannedDeliveryDate;
        refreshed.ApplyPurchaseSupplier(existing.PurchaseSupplierSelection, false);
        refreshed.ApplyDeliverySupplier(
            existing.DeliveryTargetSupplierId,
            existing.DeliverySupplierCode,
            existing.DeliverySupplierName);
        refreshed.IsInputEnabled = existing.IsInputEnabled;
        return refreshed;
    }

    /// <summary>保留已选但不再属于当前待采购范围的行，要求用户取消勾选后才能继续。</summary>
    public void MarkOutsidePurchaseScope()
    {
        RecoveryValidationMessage = "该需求已不在当前待采购范围，请取消勾选后继续。";
    }

    public void CommitPurchaseSupplier() => ApplyPurchaseSupplier(PurchaseSupplierSelection, true);
    public void ClearPurchaseSupplier() => ApplyPurchaseSupplier(null, true);

    /// <summary>
    /// 将客供件送达方锚定为父件当前实际采购供应商；父件未选择供应商时同步清空全部送达事实。
    /// </summary>
    /// <returns>任一送达供应商事实发生变化时返回 true。</returns>
    public bool SynchronizeDeliverySupplierFromParent(PurchaseCandidateItemModel parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        return parent.SupplierId == Guid.Empty
            ? ApplyDeliverySupplier(null, string.Empty, string.Empty)
            : ApplyDeliverySupplier(parent.SupplierId, parent.PurchaseSupplierCode, parent.PurchaseSupplierName);
    }

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

    /// <summary>作为一个逻辑单元更新只读送达供应商事实，避免暴露可独立编辑的半完成状态。</summary>
    private bool ApplyDeliverySupplier(Guid? supplierId, string? supplierCode, string? supplierName)
    {
        Guid? normalizedId = supplierId is { } value && value != Guid.Empty ? value : null;
        var normalizedCode = normalizedId.HasValue ? supplierCode ?? string.Empty : string.Empty;
        var normalizedName = normalizedId.HasValue ? supplierName ?? string.Empty : string.Empty;
        var changed = DeliveryTargetSupplierId != normalizedId
            || !string.Equals(DeliverySupplierCode, normalizedCode, StringComparison.Ordinal)
            || !string.Equals(DeliverySupplierName, normalizedName, StringComparison.Ordinal);
        DeliveryTargetSupplierId = normalizedId;
        DeliverySupplierCode = normalizedCode;
        DeliverySupplierName = normalizedName;
        OnPropertyChanged(nameof(DeliverySupplierDisplay));
        return changed;
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

    private static string FormatSupplier(Guid? supplierId, string? code, string? name, string fallback)
    {
        if (!supplierId.HasValue || supplierId.Value == Guid.Empty) return fallback;
        var value = $"{name}".Trim();
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
