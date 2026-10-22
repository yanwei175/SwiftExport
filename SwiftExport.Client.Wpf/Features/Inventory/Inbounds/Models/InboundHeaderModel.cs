using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Inventory.Inbounds.Models;

/// <summary>入库头编辑模型，复用基类的勾选、版本与脏状态。</summary>
public sealed partial class InboundHeaderModel : BaseLineModel<InboundHeaderDetailDto>
{
    [ObservableProperty]
    public partial string DocumentNo { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InboundDocumentTypeEnum DocumentType { get; set; } = InboundDocumentTypeEnum.PurchaseReceipt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial InboundCreationModeEnum CreationMode { get; set; } = InboundCreationModeEnum.Manual;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial InventoryDocumentLifecycleStatusEnum LifecycleStatus { get; set; } = InventoryDocumentLifecycleStatusEnum.Active;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial InboundPostingStatusEnum PostingStatus { get; set; } = InboundPostingStatusEnum.Unposted;

    [ObservableProperty]
    public partial Guid WarehouseSiteId { get; set; }

    [ObservableProperty]
    public partial string WarehouseCodeSnapshot { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WarehouseNameSnapshot { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTime ReceiptDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial string Remark { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTime? PostedDate { get; set; }

    [ObservableProperty]
    public partial string PostedBy { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTime? ReversedDate { get; set; }

    [ObservableProperty]
    public partial string ReversedBy { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReverseReason { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial Guid? TriggerPackingListId { get; set; }

    [ObservableProperty]
    public partial Guid? CommandId { get; set; }

    /// <summary>本单独立的明细编辑集合，切换头行不会丢弃未保存内容。</summary>
    public BaseListModel<InboundLineModel, InboundLineDetailDto> Lines { get; } = new(_ => new InboundLineModel());

    /// <summary>当前明细合计，仅用于页面显示，不是持久化字段。</summary>
    public decimal TotalQuantity => IsDetailLoaded ? Lines.Items.Sum(x => x.Quantity) ?? 0m : SummaryQuantity;

    /// <summary>分页摘要尚未加载明细时保持只读，不把空集合误认为完整草稿。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(TotalQuantity))]
    public partial bool IsDetailLoaded { get; set; } = true;

    /// <summary>尚未获取详情时使用服务端摘要数量。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalQuantity))]
    public partial decimal SummaryQuantity { get; set; }

    /// <summary>向编辑会话通知明细脏状态变化，不把合计写回持久化字段。</summary>
    public event EventHandler? DetailStateChanged;

    /// <summary>仅开放人工活动草稿编辑。</summary>
    public bool CanEdit => !HasVersionConflict && !IsPersistencePending && IsDetailLoaded && TriggerPackingListId is null && CreationMode == InboundCreationModeEnum.Manual && LifecycleStatus == InventoryDocumentLifecycleStatusEnum.Active && PostingStatus == InboundPostingStatusEnum.Unposted;

    /// <summary>页面按当前账号、只读模式及忙碌状态提供输入许可，不属于单据业务数据。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditInPage))]
    [NotifyPropertyChangedFor(nameof(CanChangeDocumentType))]
    public partial bool PageAllowsEditing { get; set; } = true;

    /// <summary>单元格模板同时遵循单据资格和页面输入许可。</summary>
    public bool CanEditInPage => CanEdit && PageAllowsEditing;

    /// <summary>已发送命令待确认或待详情回填时禁止编辑；只在当前会话存在。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsPersistencePending { get; set; }

    /// <summary>发生整单版本冲突后须显式重新读取，不能自动升级版本覆盖服务器。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool HasVersionConflict { get; set; }
    /// <summary>人工新单固定为采购收货，既有单据只展示真实类型，不开放类型切换。</summary>
    public bool CanChangeDocumentType => false;

    /// <summary>解释当前行的编辑限制；仅为页面显示，不持久化或替代后端校验。</summary>
    public string EditingStatus
    {
        get
        {
            if (IsPersistencePending) return "结果待恢复";
            if (HasVersionConflict) return "版本冲突，请刷新";
            if (PostingStatus == InboundPostingStatusEnum.Posted) return "已过账，只读";
            if (PostingStatus == InboundPostingStatusEnum.Reversed) return "已反过账，只读";
            if (LifecycleStatus != InventoryDocumentLifecycleStatusEnum.Active) return "非活动单，只读";
            if (CreationMode != InboundCreationModeEnum.Manual || TriggerPackingListId is not null) return "由来源业务维护";
            if (!IsDetailLoaded) return "待加载明细";
            if (!PageAllowsEditing) return "当前页面不可编辑";
            return IsNew ? "新建草稿，可编辑" : "已保存草稿，可编辑";
        }
    }

    /// <summary>明细变化只更新显示合计，脏状态仍由头行各自维护。</summary>
    public InboundHeaderModel()
    {
        Lines.ListStateChanged += (_, _) => { OnPropertyChanged(nameof(TotalQuantity)); OnPropertyChanged(nameof(CanChangeDocumentType)); DetailStateChanged?.Invoke(this, EventArgs.Empty); };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CanEdit) or nameof(IsNew))
            {
                OnPropertyChanged(nameof(CanChangeDocumentType));
                OnPropertyChanged(nameof(CanEditInPage));
            }
            if (e.PropertyName is nameof(CanEdit) or nameof(IsNew) or nameof(PageAllowsEditing))
                OnPropertyChanged(nameof(EditingStatus));
        };
    }

    /// <inheritdoc/>
    public override string? ValidateRow()
    {
        if (HasVersionConflict) return "单据版本冲突，请选中该单并点击刷新，确认放弃修改后重载。";
        if (!CanEdit) return "仅允许保存已加载的人工活动草稿。";
        if (DocumentType != InboundDocumentTypeEnum.PurchaseReceipt) return "当前仅支持采购入库草稿。";
        if (WarehouseSiteId == Guid.Empty || ReceiptDate == default) return "请选择收货仓库和收货日期。";
        if (Remark?.Length > 500) return "备注最多 500 字。";
        if (Lines.Items.Count is < 1 or > 500) return "每张草稿须包含 1–500 行。";
        if (Lines.Items.Select(x => x.SourceLineId).Distinct().Count() != Lines.Items.Count) return "同一张入库单不能重复来源。";
        return null;
    }

    /// <inheritdoc/>
    protected override bool IsTrackingIgnoredProperty(string? name) =>
        base.IsTrackingIgnoredProperty(name) || name is nameof(EditingStatus) or nameof(TotalQuantity) or nameof(CanEdit) or nameof(CanChangeDocumentType) or nameof(CanEditInPage) or nameof(PageAllowsEditing) or nameof(IsDetailLoaded) or nameof(SummaryQuantity) or nameof(IsPersistencePending) or nameof(HasVersionConflict);

    /// <inheritdoc/>
    protected override void LoadFromDto(InboundHeaderDetailDto dto) => dto.Adapt(this);

    /// <inheritdoc/>
    public override InboundHeaderDetailDto ToDto() => this.Adapt<InboundHeaderDetailDto>();
}
