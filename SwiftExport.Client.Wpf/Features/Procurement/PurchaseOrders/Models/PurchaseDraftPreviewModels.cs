using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;

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
    public Guid? CustomerId { get; init; }
    public string CustomerCode { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string? QuantityError => PurchaseQty <= 0 || PurchaseQty > ExpectedRemainingQty
        ? $"本次数量必须大于 0 且不超过 {ExpectedRemainingQty:N4}。"
        : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuantityError))]
    public partial decimal PurchaseQty { get; set; }

    [ObservableProperty]
    public partial decimal UnitPrice { get; set; }

    [ObservableProperty]
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
        UnitPrice = UnitPrice,
        TaxPrice = TaxPrice,
        PlannedDeliveryDate = PlannedDeliveryDate,
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

/// <summary>请求采购准备页按稳定需求身份恢复候选，并说明触发恢复的结构化原因。</summary>
public sealed class PurchaseDraftCandidateRecoveryRequestedEventArgs : EventArgs
{
    public string ErrorCode { get; init; } = string.Empty;
    /// <summary>本会话已经成功或幂等应用的来源键；完整范围恢复时这些来源不得重新选中。</summary>
    public IReadOnlyList<string> CompletedSourceKeys { get; init; } = [];
}

/// <summary>采购单审核阶段的显式单据模型，不依赖分组首行推断单头。</summary>
public sealed partial class PurchaseDraftDocumentReviewModel
{
    public string SourceType { get; init; } = string.Empty;
    public Guid SourceUid { get; init; }
    public string SourceNo { get; init; } = string.Empty;
    public Guid SupplierId { get; init; }
    public string SupplierDisplay { get; init; } = string.Empty;
    public Guid? CustomerId { get; init; }
    public string CustomerDisplay { get; init; } = string.Empty;
    public PurchaseDraftTargetSelectionModel Target { get; init; } = null!;
    public PurchaseDraftHeaderReviewModel Header { get; init; } = new();
    public IReadOnlyList<PurchaseDraftPreviewLineModel> Lines { get; init; } = [];
    public string DocumentKey => $"{SourceType.Trim().ToUpperInvariant()}|{SourceUid:N}|{SupplierId:N}";
}

/// <summary>采购审核页的只读单头快照；所有业务字段均来自后端可信预览。</summary>
public sealed partial class PurchaseDraftHeaderReviewModel
{
    public string OrderNo { get; set; } = string.Empty;
    public string CustomerPoNo { get; set; } = string.Empty;
    public string BoNo { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal? DiscountRate { get; set; }
    public decimal? DiscountAmount { get; set; }
    public string DiscountRemark { get; set; } = string.Empty;
    public decimal UntaxedTotalAmount { get; set; }
    public decimal TaxIncludedTotalAmount { get; set; }
    public DateTime? OrderDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public string InletHoseType { get; set; } = string.Empty;
    public string AeratorType { get; set; } = string.Empty;
    public string PalletType { get; set; } = string.Empty;
    public string BoxType { get; set; } = string.Empty;
    public string LabelType { get; set; } = string.Empty;
    public string InstructionManualType { get; set; } = string.Empty;
    public string CartonType { get; set; } = string.Empty;
    public string LogoType { get; set; } = string.Empty;
    public string SalesPerson { get; set; } = string.Empty;
    public string PurchasePerson { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public string OrderNoDisplay => string.IsNullOrWhiteSpace(OrderNo) ? "创建时按现有规则生成" : OrderNo;
    public string OrderDateDisplay => OrderDate.HasValue ? OrderDate.Value.ToString("yyyy-MM-dd") : "创建时取系统日期";
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
    /// <summary>服务端返回的完整影响范围，包含本次未勾选但受确认规则覆盖的关联需求。</summary>
    public IReadOnlyList<PurchaseDraftWarningAffectedObjectDto> AffectedObjects { get; init; } = [];
    /// <summary>当前警告是否携带可向采购员展示的稳定影响对象。</summary>
    public bool HasAffectedObjects => AffectedObjects.Count > 0;
    /// <summary>当前可辨认的受影响对象总数。</summary>
    public int AffectedCount => AffectedObjects.Count > 0
        ? AffectedObjects.Count
        : Math.Max(NetDemandIds.Count, SupplierIds.Count);
    public bool CanLocate => AffectedItems.Count > 0;
    /// <summary>逐对象展示文本，明确区分本次已选与关联未选。</summary>
    public IReadOnlyList<string> AffectedObjectLines => AffectedObjects.Count == 0
        ? ["受影响对象辨认信息不完整；请刷新候选后重试。"]
        : AffectedObjects.Select(FormatAffectedObject).ToArray();
    public string AffectedObjectText => string.Join("；", AffectedObjectLines);
    public string ActionText => Code switch
    {
        PurchaseDraftWarningCodes.InvalidPurchaseQuantity => "处理：回到本次计划，修正采购数量后重新预览。",
        PurchaseDraftWarningCodes.PurchaseSupplierRequired => "处理：在本次计划中选择实际采购供应商后重新预览。",
        PurchaseDraftWarningCodes.PurchaseSupplierInvalid => "处理：原供应商已失效，请重新选择有效的实际采购供应商。",
        PurchaseDraftWarningCodes.DeliveryTargetRequired => "处理：为客供件明确选择实际送达方；若父件关系无效，请刷新或移除该选择。",
        PurchaseDraftWarningCodes.CorruptedPlanningState => "处理：刷新采购候选；若仍存在，请移除该选择并联系计划人员核对来源事实。",
        PurchaseDraftWarningCodes.PurchaseDataIncomplete => "处理：允许创建草稿；创建后可在采购详情补齐供应商产品资料。",
        PurchaseDraftWarningCodes.DifferentPlannedDeliveryDates => "处理：核对展开明细中的原交期；确认采用最早交期后方可创建。",
        PurchaseDraftWarningCodes.RelatedCustomerSuppliedRemaining => "已选父件仍有关联客供件未选，本次不会采购这些客供件；可勾选后重新校验，或确认仅采购当前选择后继续。",
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
        var affectedObjects = dtos
            .SelectMany(dto => dto.AffectedObjects ?? [])
            .GroupBy(item => item.NetDemandId)
            .Select(group => group.First())
            .OrderBy(item => item.SourceNo, StringComparer.Ordinal)
            .ThenBy(item => item.SourceLineNumber ?? int.MaxValue)
            .ThenBy(item => item.NetDemandId)
            .ToArray();
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
            SourceNo = affectedObjects.Select(item => item.SourceNo).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? affected.Select(item => item.SourceNo).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? string.Empty,
            NetDemandIds = netDemandIds,
            SupplierIds = dtos.Where(dto => dto.SupplierId.HasValue)
                .Select(dto => dto.SupplierId!.Value)
                .Distinct()
                .OrderBy(id => id)
                .ToArray(),
            AffectedItems = affected,
            AffectedObjects = affectedObjects,
        };
    }

    /// <summary>把结构化影响对象转换为采购员可辨认的紧凑文本，明确区分本次已选与关联未选。</summary>
    private static string FormatAffectedObject(PurchaseDraftWarningAffectedObjectDto item)
    {
        var selection = item.IsIncludedInCurrentPurchase ? "本次已选" : "关联未选";
        var line = item.SourceLineNumber.HasValue ? $"行 {item.SourceLineNumber.Value}" : "无行号";
        var model = string.IsNullOrWhiteSpace(item.LinkSku) ? "无型号" : item.LinkSku;
        return $"[{selection}] {item.SourceNo}/{line}/{model}/{item.Sku}，选择后剩余 {item.RemainingAfterSelectionQty:N4}";
    }
}

/// <summary>组合服务端采购行与所属来源/实际供应商，供预览表格只读展示。</summary>
public sealed partial class PurchaseDraftPreviewLineModel
{
    public string SourceType { get; set; } = string.Empty;
    /// <summary>来源业务稳定身份，用于页面分组而不作为主要显示文本。</summary>
    public Guid SourceUid { get; set; }
    public string SourceNo { get; set; } = string.Empty;
    /// <summary>实际采购供应商稳定身份，用于避免同名供应商被误分为一组。</summary>
    public Guid SupplierId { get; set; }
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
    public string SupplierDisplay => $"{SupplierName}".Trim();
    /// <summary>来源与实际供应商组成的稳定分组键。</summary>
    public string SourceSupplierGroupKey => $"{SourceType.Trim().ToUpperInvariant()}|{SourceUid:N}|{SupplierId:N}";
    /// <summary>预览分组的采购业务标题。</summary>
    public string SourceSupplierGroupLabel => $"来源 {SourceNo} · 实际采购供应商 {SupplierDisplay}";
    public string AttributeSummary => string.Join(" / ", new[] { Color, Specification, MaterialType }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
    public string DeliveryModeText => IsCustomerSupplied ? "客供送达" : "标准交付";
}

public sealed partial class PurchaseDraftCreateResultModel
{
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
        SourceType = dto.SourceType ?? string.Empty,
        SourceUid = dto.SourceUid,
        SourceNo = sourceNo,
        Outcome = dto.Outcome,
        ErrorCode = dto.ErrorCode ?? string.Empty,
        Message = dto.Message ?? string.Empty,
        PurchaseOrderNos = string.Join(", ", dto.PurchaseDrafts.Select(item => item.OrderNo).Where(item => !string.IsNullOrWhiteSpace(item))),
    };
}
