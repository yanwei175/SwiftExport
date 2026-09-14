using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Inventory.Outbounds.Models;

/// <summary>出库头编辑模型，复用基类的勾选、版本与脏状态。</summary>
public sealed partial class OutboundHeaderModel : BaseLineModel<OutboundHeaderDetailDto>
{
    [ObservableProperty]
    public partial string DocumentNo { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial OutboundDocumentTypeEnum DocumentType { get; set; } = OutboundDocumentTypeEnum.StockIssue;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial InventoryDocumentLifecycleStatusEnum LifecycleStatus { get; set; } = InventoryDocumentLifecycleStatusEnum.Active;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial OutboundPostingStatusEnum PostingStatus { get; set; } = OutboundPostingStatusEnum.Unposted;

    [ObservableProperty]
    public partial Guid WarehouseSiteId { get; set; }

    [ObservableProperty]
    public partial string WarehouseCodeSnapshot { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WarehouseNameSnapshot { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTime ShipmentDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial string Remark { get; set; } = string.Empty;

    /// <summary>草稿用途说明；可暂存空值，服务端过账时强制校验。</summary>
    [ObservableProperty]
    public partial string IssueReason { get; set; } = string.Empty;

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
    public BaseListModel<OutboundLineModel, OutboundLineDetailDto> Lines { get; } = new(_ => new OutboundLineModel());

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
    public bool CanEdit => !IsPersistencePending && IsDetailLoaded && TriggerPackingListId is null && DocumentType == OutboundDocumentTypeEnum.StockIssue && LifecycleStatus == InventoryDocumentLifecycleStatusEnum.Active && PostingStatus == OutboundPostingStatusEnum.Unposted;

    /// <summary>已发送命令待确认或待详情回填时禁止编辑；只在当前会话存在。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsPersistencePending { get; set; }

    /// <summary>发生整单版本冲突后须显式重新读取，不能自动升级版本覆盖服务器。</summary>
    [ObservableProperty] public partial bool HasVersionConflict { get; set; }
    /// <summary>人工单固定普通库存出库类型，不允许切换成客户交付。</summary>
    public bool CanChangeDocumentType => false;

    /// <summary>明细变化只更新显示合计，脏状态仍由头行各自维护。</summary>
    public OutboundHeaderModel()
    {
        Lines.ListStateChanged += (_, _) => { OnPropertyChanged(nameof(TotalQuantity)); OnPropertyChanged(nameof(CanChangeDocumentType)); DetailStateChanged?.Invoke(this, EventArgs.Empty); };
        PropertyChanged += (_, e) => { if (e.PropertyName is nameof(CanEdit) or nameof(IsNew)) OnPropertyChanged(nameof(CanChangeDocumentType)); };
    }

    /// <inheritdoc/>
    public override string? ValidateRow()
    {
        if (!CanEdit) return "仅允许保存已加载的人工活动草稿。";
        if (HasVersionConflict) return "单据版本冲突，请选中该单并点击刷新，确认放弃修改后重载。";
        if (DocumentType != OutboundDocumentTypeEnum.StockIssue) return "当前仅支持普通库存出库草稿。";
        if (WarehouseSiteId == Guid.Empty || ShipmentDate == default) return "请选择出库仓库和出库日期。";
        if (Remark?.Length > 500) return "备注最多 500 字。";
        if (IssueReason?.Length > 500) return "出库原因最多 500 字。";
        if (Lines.Items.Count > 500) return "每张草稿最多包含 500 行。";
        if (Lines.Items.Select(x => x.ProductId).Distinct().Count() != Lines.Items.Count) return "同一张出库单不能重复产品。";
        return null;
    }

    /// <inheritdoc/>
    protected override bool IsTrackingIgnoredProperty(string? name) =>
        base.IsTrackingIgnoredProperty(name) || name is nameof(TotalQuantity) or nameof(CanEdit) or nameof(CanChangeDocumentType) or nameof(IsDetailLoaded) or nameof(SummaryQuantity) or nameof(IsPersistencePending) or nameof(HasVersionConflict);

    /// <inheritdoc/>
    protected override void LoadFromDto(OutboundHeaderDetailDto dto) => dto.Adapt(this);

    /// <inheritdoc/>
    public override OutboundHeaderDetailDto ToDto() => this.Adapt<OutboundHeaderDetailDto>();
}
