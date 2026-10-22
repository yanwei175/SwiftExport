using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using System.Text.Json.Serialization;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>准备页捕获完整候选选择和入口信息，不承载服务端预览。</summary>
public sealed class PurchaseDraftPreparationRequest
{
    public PurchaseDraftEntryModeEnum EntryMode { get; init; }
    public IReadOnlyList<PurchaseCandidateItemModel> Rows { get; init; } = [];
    public string SourceType { get; init; } = string.Empty;
    public Guid? SourceUid { get; init; }
    public string SourceNo { get; init; } = string.Empty;
}

/// <summary>冻结本地校验后的采购输入及需求身份。</summary>
public sealed partial class PurchaseDraftSelectionItemModel : ObservableObject
{
    public Guid CommandItemId { get; init; } = Guid.NewGuid();
    public Guid NetDemandId { get; init; }
    public int ExpectedVersion { get; init; }
    public decimal ExpectedRemainingQty { get; init; }
    [JsonInclude] public Guid SupplierId { get; private set; }
    [JsonInclude] public Guid? DeliveryTargetSupplierId { get; private set; }
    public string SourceType { get; init; } = string.Empty;
    public Guid SourceUid { get; init; }
    public string SourceNo { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public string LinkSku { get; init; } = string.Empty;
    public bool IsCustomerSupplied { get; init; }
    public Guid? SuggestedPurchaseSupplierId { get; init; }
    public string SuggestedPurchaseSupplierCode { get; init; } = string.Empty;
    public string SuggestedPurchaseSupplierName { get; init; } = string.Empty;
    public Guid? SuggestedDeliverySupplierId { get; init; }
    public string SuggestedDeliverySupplierCode { get; init; } = string.Empty;
    public string SuggestedDeliverySupplierName { get; init; } = string.Empty;
    public string SuggestedDeliverySupplierHint { get; init; } = string.Empty;
    public IReadOnlyList<PurchaseDraftActualSupplierFactDto> ParentActualPurchaseSuppliers { get; init; } = [];
    public bool IsQuantityReadOnly { get; init; }
    public Guid? CustomerId { get; init; }
    public string CustomerCode { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    /// <summary>当前可创建量及四位正数数量的本地错误。</summary>
    public string? QuantityError => PurchaseDraftInputValidation.ValidateQuantity(PurchaseQty, ExpectedRemainingQty);
    /// <summary>准备快照在提交前重验数量、两价和金额范围，不依赖候选页先前校验。</summary>
    public string? InputError => PurchaseDraftInputValidation.Validate(PurchaseQty, ExpectedRemainingQty, UnitPrice, TaxPrice);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuantityError))]
    [NotifyPropertyChangedFor(nameof(InputError))]
    public partial decimal PurchaseQty { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InputError))]
    public partial decimal UnitPrice { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InputError))]
    public partial decimal TaxPrice { get; set; }

    [ObservableProperty]
    public partial DateTime? PlannedDeliveryDate { get; set; }

    [ObservableProperty]
    public partial string PurchaseSupplierSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ProcurementSupplierCandidateDto? PurchaseSupplierSelection { get; set; }

    [ObservableProperty]
    public partial string DeliverySupplierSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ProcurementSupplierCandidateDto? DeliverySupplierSelection { get; set; }

    public string SuggestedPurchaseSupplierDisplay => FormatSupplier(
        SuggestedPurchaseSupplierCode, SuggestedPurchaseSupplierName, "未提供建议");
    public string SuggestedDeliverySupplierDisplay => IsCustomerSupplied
        ? FormatSupplier(SuggestedDeliverySupplierCode, SuggestedDeliverySupplierName, "未提供建议")
        : "不适用";
    public bool RequiresExplicitDeliverySelection => IsCustomerSupplied && !DeliveryTargetSupplierId.HasValue;
    public string ParentActualSupplierEvidence => !IsCustomerSupplied
        ? "不适用：标准交付。"
        : ParentActualPurchaseSuppliers.Count == 0
        ? "未找到父件活动采购单，无法从执行事实推断送达方。"
        : string.Join("；", ParentActualPurchaseSuppliers.Select(fact =>
            $"{FormatSupplier(fact.SupplierCode, fact.SupplierName, fact.SupplierId.ToString())}（采购单 {fact.PurchaseOrderNos}）"));
    public string DeliverySelectionGuidance => !IsCustomerSupplied
        ? "标准交付，无需单独选择送达供应商。"
        : DeliveryTargetSupplierId.HasValue
            ? "实际送达方由推荐送达方初始化，可由采购明确修正；父件历史采购方仅供追溯，不覆盖本次输入。"
            : string.IsNullOrWhiteSpace(SuggestedDeliverySupplierHint)
                ? "当前没有可采用的推荐送达方，请明确选择本次实际送达方。"
                : SuggestedDeliverySupplierHint;

    public event EventHandler? ValueChanged;

    partial void OnPurchaseQtyChanged(decimal value) => ValueChanged?.Invoke(this, EventArgs.Empty);
    partial void OnUnitPriceChanged(decimal value) => ValueChanged?.Invoke(this, EventArgs.Empty);
    partial void OnTaxPriceChanged(decimal value) => ValueChanged?.Invoke(this, EventArgs.Empty);
    partial void OnPlannedDeliveryDateChanged(DateTime? value) => ValueChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>初始化实际采购方与送达方；多项父件实际采购事实绝不自动取第一项。</summary>
    public void InitializeSupplierSelections()
    {
        var purchase = SuggestedPurchaseSupplierId.HasValue
            ? new ProcurementSupplierCandidateDto
            {
                Uid = SuggestedPurchaseSupplierId.Value,
                SupplierCode = SuggestedPurchaseSupplierCode,
                SupplierName = SuggestedPurchaseSupplierName,
            }
            : null;
        ProcurementSupplierCandidateDto? delivery = null;
        if (IsCustomerSupplied && SuggestedDeliverySupplierId.HasValue)
        {
            delivery = new ProcurementSupplierCandidateDto
            {
                Uid = SuggestedDeliverySupplierId.Value,
                SupplierCode = SuggestedDeliverySupplierCode,
                SupplierName = SuggestedDeliverySupplierName,
            };
        }

        ApplyPurchaseSupplier(purchase, notify: false);
        ApplyDeliverySupplier(delivery, notify: false);
    }

    /// <summary>采用 Lookup 已提交的实际采购供应商，不回写物料需求建议。</summary>
    public void CommitPurchaseSupplier() => ApplyPurchaseSupplier(PurchaseSupplierSelection, notify: true);

    /// <summary>清除实际采购供应商，使本地校验返回可定位的采购方缺失原因。</summary>
    public void ClearPurchaseSupplier() => ApplyPurchaseSupplier(null, notify: true);

    /// <summary>采用 Lookup 已提交的客供件实际送达方，与采购方保持独立。</summary>
    public void CommitDeliverySupplier() => ApplyDeliverySupplier(DeliverySupplierSelection, notify: true);

    /// <summary>清除客供件实际送达方，使服务端重新校验该输入。</summary>
    public void ClearDeliverySupplier() => ApplyDeliverySupplier(null, notify: true);

    private void ApplyPurchaseSupplier(ProcurementSupplierCandidateDto? supplier, bool notify)
    {
        var nextId = supplier?.Uid ?? Guid.Empty;
        var changed = SupplierId != nextId;
        PurchaseSupplierSelection = supplier;
        SupplierId = nextId;
        PurchaseSupplierSearchText = supplier is null
            ? string.Empty
            : FormatSupplier(supplier.SupplierCode, supplier.SupplierName, supplier.Uid.ToString());
        if (notify && changed) ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyDeliverySupplier(ProcurementSupplierCandidateDto? supplier, bool notify)
    {
        var changed = DeliveryTargetSupplierId != supplier?.Uid;
        DeliverySupplierSelection = supplier;
        DeliveryTargetSupplierId = supplier?.Uid;
        DeliverySupplierSearchText = supplier is null
            ? string.Empty
            : FormatSupplier(supplier.SupplierCode, supplier.SupplierName, supplier.Uid.ToString());
        if (notify && changed) ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string FormatSupplier(string? code, string? name, string fallback)
    {
        var display = $"{code} {name}".Trim();
        return string.IsNullOrWhiteSpace(display) ? fallback : display;
    }


}

/// <summary>采购员可明确选择的新建或既有草稿目标。</summary>
public sealed class PurchaseDraftTargetChoiceModel
{
    /// <summary>原追加目标不可核实时保留显示，但不能提交该选择。</summary>
    public bool IsAvailable { get; init; } = true;
    public PurchaseDraftTargetModeEnum TargetMode { get; init; }
    public Guid? ExistingPurchaseOrderId { get; init; }
    public int? ExpectedPurchaseOrderVersion { get; init; }
    public string DisplayText { get; init; } = string.Empty;
}

/// <summary>同一来源供应商的目标选择，保留不可核实的原选择。</summary>
public sealed partial class PurchaseDraftTargetSelectionModel : ObservableObject
{
    private bool _suppress;

    public string SourceType { get; init; } = string.Empty;
    public Guid SourceUid { get; init; }
    public string SourceNo { get; init; } = string.Empty;
    public Guid SupplierId { get; init; }
    public string SupplierName { get; init; } = string.Empty;
    public IReadOnlyList<PurchaseDraftTargetChoiceModel> Choices { get; init; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedChoiceDisplayText))]
    [NotifyPropertyChangedFor(nameof(IsCreateNew))]
    public partial PurchaseDraftTargetChoiceModel? SelectedChoice { get; set; }

    /// <summary>是否存在需要采购员选择的多个合法草稿目标。</summary>
    public bool HasMultipleChoices => Choices.Count > 1;
    public bool IsCreateNew => SelectedChoice?.TargetMode != PurchaseDraftTargetModeEnum.AppendExisting;

    /// <summary>当前草稿目标的只读业务名称。</summary>
    public string SelectedChoiceDisplayText => SelectedChoice?.DisplayText ?? "新建采购草稿";

    public event EventHandler? ValueChanged;

    public void SetInitialChoice(PurchaseDraftTargetChoiceModel choice)
    {
        _suppress = true;
        SelectedChoice = choice;
        _suppress = false;
    }

    partial void OnSelectedChoiceChanged(PurchaseDraftTargetChoiceModel? value)
    {
        if (!_suppress)
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }



    public CreatePurchaseDraftTargetCommand ToCreateDto() => new()
    {
        SupplierId = SupplierId,
        TargetMode = SelectedChoice?.TargetMode ?? PurchaseDraftTargetModeEnum.CreateNew,
        ExistingPurchaseOrderId = SelectedChoice?.ExistingPurchaseOrderId,
        ExpectedPurchaseOrderVersion = SelectedChoice?.ExpectedPurchaseOrderVersion,
    };
}

/// <summary>逐来源创建结果，保留诊断和实际采购引用。</summary>
public sealed partial class PurchaseDraftCreateResultModel
{
    /// <summary>可按稳定身份逐张打开的实际采购草稿。</summary>
    public IReadOnlyList<CreatedPurchaseDraftDto> PurchaseDrafts { get; init; } = [];
    public string SourceType { get; init; } = string.Empty;
    public Guid SourceUid { get; init; }
    /// <summary>创建前保存的来源单号，用于结果阶段辨认。</summary>
    public string SourceNo { get; init; } = string.Empty;
    public NetDemandCommandOutcomeEnum Outcome { get; init; }
    public string ErrorCode { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string PurchaseOrderNos { get; init; } = string.Empty;
    /// <summary>来源类型与业务单号的组合显示。</summary>
    public string SourceDisplay => string.IsNullOrWhiteSpace(SourceNo) ? SourceType : $"{SourceType} · {SourceNo}";
    /// <summary>面向采购员的创建结果名称。</summary>
    public string OutcomeText => Outcome switch
    {
        NetDemandCommandOutcomeEnum.Succeeded => "创建成功",
        NetDemandCommandOutcomeEnum.AlreadyApplied => "已安全重放",
        _ => "创建失败",
    };
    /// <summary>仅在失败返回稳定代码时显示的辅助技术详情。</summary>
    public string TechnicalDetail => string.IsNullOrWhiteSpace(ErrorCode) ? string.Empty : $"错误代码：{ErrorCode}";

    /// <summary>建立逐来源创建结果，并保留调用前已知的来源单号用于业务辨认。</summary>
    public static PurchaseDraftCreateResultModel FromDto(CreatePurchaseDraftSourceResult dto, string sourceNo) => new()
    {
        PurchaseDrafts = (dto.PurchaseDrafts ?? []).ToArray(),
        SourceType = dto.SourceType ?? string.Empty,
        SourceUid = dto.SourceUid,
        SourceNo = sourceNo,
        Outcome = dto.Outcome,
        ErrorCode = dto.ErrorCode ?? string.Empty,
        Message = dto.Message ?? string.Empty,
        PurchaseOrderNos = string.Join(", ", (dto.PurchaseDrafts ?? []).Select(item => item.OrderNo).Where(item => !string.IsNullOrWhiteSpace(item))),
    };
}
