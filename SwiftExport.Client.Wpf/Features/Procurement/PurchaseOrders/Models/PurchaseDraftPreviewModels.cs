using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

public sealed class PurchaseDraftPreviewOpenRequest
{
    public PurchaseDraftEntryModeEnum EntryMode { get; init; }
    public IReadOnlyList<PurchaseCandidateItemModel> Rows { get; init; } = [];
    public string SourceType { get; init; } = string.Empty;
    public Guid? SourceUid { get; init; }
    public string SourceNo { get; init; } = string.Empty;
}

public sealed partial class PurchaseDraftSelectionItemModel : ObservableObject
{
    public Guid CommandItemId { get; init; } = Guid.NewGuid();
    public Guid NetDemandId { get; init; }
    public int ExpectedVersion { get; init; }
    public decimal ExpectedRemainingQty { get; init; }
    public Guid SupplierId { get; private set; }
    public Guid? DeliveryTargetSupplierId { get; private set; }
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
    public string? QuantityError => PurchaseQty <= 0 || PurchaseQty > ExpectedRemainingQty
        ? $"本次数量必须大于 0 且不超过 {ExpectedRemainingQty:N4}。"
        : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuantityError))]
    public partial decimal PurchaseQty { get; set; }

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
    public bool RequiresExplicitDeliverySelection => IsCustomerSupplied
        && (ParentActualPurchaseSuppliers.Count > 1
            || (ParentActualPurchaseSuppliers.Count == 1 && !ParentActualPurchaseSuppliers[0].IsActive));
    public string ParentActualSupplierEvidence => !IsCustomerSupplied
        ? "不适用：标准交付。"
        : ParentActualPurchaseSuppliers.Count == 0
        ? "未找到父件活动采购单，无法从执行事实推断送达方。"
        : string.Join("；", ParentActualPurchaseSuppliers.Select(fact =>
            $"{FormatSupplier(fact.SupplierCode, fact.SupplierName, fact.SupplierId.ToString())}（采购单 {fact.PurchaseOrderNos}）"));
    public string DeliverySelectionGuidance => !IsCustomerSupplied
        ? "标准交付，无需单独选择送达供应商。"
        : ParentActualPurchaseSuppliers.Count > 1
            ? "父件存在多个实际采购方，必须明确选择本次实际送达方，系统不会任取一个。"
            : ParentActualPurchaseSuppliers.Count == 1 && !ParentActualPurchaseSuppliers[0].IsActive
                ? "父件唯一实际采购供应商当前已失效，不能自动采用；请明确选择有效送达方。"
                : ParentActualPurchaseSuppliers.Count == 1
                ? "实际送达方依据父件唯一活动采购单预填；可由采购明确修正。"
                : string.IsNullOrWhiteSpace(SuggestedDeliverySupplierHint)
                    ? "暂无父件实际采购事实；当前建议仅供对照，请核对实际送达方。"
                    : SuggestedDeliverySupplierHint;

    public event EventHandler? ValueChanged;

    partial void OnPurchaseQtyChanged(decimal value) => ValueChanged?.Invoke(this, EventArgs.Empty);

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
        var activeActual = ParentActualPurchaseSuppliers.Where(fact => fact.IsActive).ToArray();
        if (IsCustomerSupplied && ParentActualPurchaseSuppliers.Count == 1 && activeActual.Length == 1)
        {
            delivery = new ProcurementSupplierCandidateDto
            {
                Uid = activeActual[0].SupplierId,
                SupplierCode = activeActual[0].SupplierCode,
                SupplierName = activeActual[0].SupplierName,
            };
        }
        else if (IsCustomerSupplied && ParentActualPurchaseSuppliers.Count == 0 && SuggestedDeliverySupplierId.HasValue)
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

    /// <summary>清除实际采购供应商，使服务端预览返回可定位的采购方阻断。</summary>
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

    public PurchaseDraftPreviewItem ToDto() => new()
    {
        CommandItemId = CommandItemId,
        NetDemandId = NetDemandId,
        ExpectedVersion = ExpectedVersion,
        ExpectedRemainingQty = ExpectedRemainingQty,
        PurchaseQty = PurchaseQty,
        SupplierId = SupplierId,
        DeliveryTargetSupplierId = DeliveryTargetSupplierId,
    };
}

public sealed class PurchaseDraftTargetChoiceModel
{
    public PurchaseDraftTargetModeEnum TargetMode { get; init; }
    public Guid? ExistingPurchaseOrderId { get; init; }
    public int? ExpectedPurchaseOrderVersion { get; init; }
    public string DisplayText { get; init; } = string.Empty;
}

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
    public partial PurchaseDraftTargetChoiceModel? SelectedChoice { get; set; }

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

    public PurchaseDraftTargetSelectionDto ToPreviewDto() => new()
    {
        SourceType = SourceType,
        SourceUid = SourceUid,
        SupplierId = SupplierId,
        TargetMode = SelectedChoice?.TargetMode ?? PurchaseDraftTargetModeEnum.CreateNew,
        ExistingPurchaseOrderId = SelectedChoice?.ExistingPurchaseOrderId,
        ExpectedPurchaseOrderVersion = SelectedChoice?.ExpectedPurchaseOrderVersion,
    };

    public CreatePurchaseDraftTargetCommand ToCreateDto() => new()
    {
        SupplierId = SupplierId,
        TargetMode = SelectedChoice?.TargetMode ?? PurchaseDraftTargetModeEnum.CreateNew,
        ExistingPurchaseOrderId = SelectedChoice?.ExistingPurchaseOrderId,
        ExpectedPurchaseOrderVersion = SelectedChoice?.ExpectedPurchaseOrderVersion,
    };
}

public sealed partial class PurchaseDraftWarningModel : ObservableObject
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool IsBlocker { get; init; }
    public bool RequiresConfirmation { get; init; }
    public string SourceType { get; init; } = string.Empty;
    public Guid? SourceUid { get; init; }
    public string SourceNo { get; init; } = string.Empty;
    public IReadOnlyList<Guid> NetDemandIds { get; init; } = [];
    public IReadOnlyList<Guid> SupplierIds { get; init; } = [];
    public IReadOnlyList<PurchaseDraftSelectionItemModel> AffectedItems { get; init; } = [];
    public int AffectedCount => Math.Max(NetDemandIds.Count, SupplierIds.Count);
    public bool CanLocate => AffectedItems.Count > 0;
    public string AffectedObjectText => AffectedItems.Count == 0
        ? "当前返回未包含可定位行；请刷新候选后重试。"
        : string.Join("；", AffectedItems.Select(item =>
            $"{item.SourceNo}/{(string.IsNullOrWhiteSpace(item.LinkSku) ? "无型号" : item.LinkSku)}/{item.Sku}"));
    public string ActionText => Code switch
    {
        PurchaseDraftWarningCodes.InvalidPurchaseQuantity => "处理：回到本次计划，修正采购数量后重新预览。",
        PurchaseDraftWarningCodes.PurchaseSupplierRequired => "处理：在本次计划中选择实际采购供应商后重新预览。",
        PurchaseDraftWarningCodes.PurchaseSupplierInvalid => "处理：原供应商已失效，请重新选择有效的实际采购供应商。",
        PurchaseDraftWarningCodes.DeliveryTargetRequired => "处理：为客供件明确选择实际送达方；若父件关系无效，请刷新或移除该选择。",
        PurchaseDraftWarningCodes.CorruptedPlanningState => "处理：刷新采购候选；若仍存在，请移除该选择并联系计划人员核对来源事实。",
        PurchaseDraftWarningCodes.PurchaseDataIncomplete => "处理：允许创建草稿；创建后在采购详情补齐供应商产品或价格资料。",
        PurchaseDraftWarningCodes.DifferentPlannedDeliveryDates => "处理：核对展开明细中的原交期；确认采用最早交期后方可创建。",
        PurchaseDraftWarningCodes.RelatedCustomerSuppliedRemaining => "处理：核对受影响父件和客供件；确认本次仅采购当前选择后方可创建。",
        _ => "处理：按提示核对受影响对象后重新预览。",
    };
    public string ConfirmationScopeText => RequiresConfirmation
        ? $"一次确认覆盖来源 {SourceNo} 下当前列出的 {AffectedCount} 个受影响对象。"
        : string.Empty;
    public string TechnicalDetail => $"代码：{Code}";

    [ObservableProperty]
    public partial bool IsConfirmed { get; set; }

    public event EventHandler? ConfirmationChanged;

    partial void OnIsConfirmedChanged(bool value) => ConfirmationChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>按后端确认协议聚合同一来源与代码，并合并全部影响身份而非只取第一条。</summary>
    public static PurchaseDraftWarningModel FromDtos(
        IReadOnlyList<PurchaseDraftWarningDto> dtos,
        IReadOnlyList<PurchaseDraftSelectionItemModel> items)
    {
        var first = dtos[0];
        var netDemandIds = dtos.SelectMany(dto => dto.NetDemandIds ?? [])
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        var affected = items.Where(item => netDemandIds.Contains(item.NetDemandId)).ToArray();
        return new PurchaseDraftWarningModel
        {
            Code = first.Code ?? string.Empty,
            Message = string.Join("；", dtos.Select(dto => dto.Message ?? string.Empty)
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.Ordinal)),
            IsBlocker = dtos.Any(dto => dto.IsBlocker),
            RequiresConfirmation = dtos.Any(dto => dto.RequiresConfirmation),
            SourceType = first.SourceType ?? string.Empty,
            SourceUid = first.SourceUid,
            SourceNo = affected.Select(item => item.SourceNo).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
            NetDemandIds = netDemandIds,
            SupplierIds = dtos.Where(dto => dto.SupplierId.HasValue)
                .Select(dto => dto.SupplierId!.Value)
                .Distinct()
                .OrderBy(id => id)
                .ToArray(),
            AffectedItems = affected,
        };
    }
}

/// <summary>组合服务端采购行与所属来源/实际供应商，供预览表格只读展示。</summary>
public sealed class PurchaseDraftPreviewLineModel
{
    public string SourceType { get; set; } = string.Empty;
    public string SourceNo { get; set; } = string.Empty;
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string LinkSku { get; set; } = string.Empty;
    public string CustomerSku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public string MaterialType { get; set; } = string.Empty;
    public bool IsCustomerSupplied { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string SupplierSku { get; set; } = string.Empty;
    public decimal? UnitPrice { get; set; }
    public decimal? TaxPrice { get; set; }
    public decimal PurchaseQty { get; set; }
    public DateTime? PlannedDeliveryDate { get; set; }
    public string DeliveryTargetSupplierName { get; set; } = string.Empty;
    public bool PurchaseDataIncomplete { get; set; }
    public string MergeDifferenceReason { get; set; } = string.Empty;
    public List<DateTime?> SourcePlannedDeliveryDates { get; set; } = [];
    public List<PurchaseDraftSourceContributionDto> Contributions { get; set; } = [];
    public int ContributionCount => Contributions.Count;
    public string SupplierDisplay => $"{SupplierCode} {SupplierName}".Trim();
    public string AttributeSummary => string.Join(" / ", new[] { Color, Specification, MaterialType }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
    public string DeliveryModeText => IsCustomerSupplied ? "客供送达" : "标准交付";
}

public sealed class PurchaseDraftCreateResultModel
{
    public string SourceType { get; init; } = string.Empty;
    public Guid SourceUid { get; init; }
    public NetDemandCommandOutcomeEnum Outcome { get; init; }
    public string ErrorCode { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string PurchaseOrderNos { get; init; } = string.Empty;

    public static PurchaseDraftCreateResultModel FromDto(CreatePurchaseDraftSourceResult dto) => new()
    {
        SourceType = dto.SourceType ?? string.Empty,
        SourceUid = dto.SourceUid,
        Outcome = dto.Outcome,
        ErrorCode = dto.ErrorCode ?? string.Empty,
        Message = dto.Message ?? string.Empty,
        PurchaseOrderNos = string.Join(", ", dto.PurchaseDrafts.Select(item => item.OrderNo).Where(item => !string.IsNullOrWhiteSpace(item))),
    };
}
