using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
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
    public partial Guid? TriggerPackingListId { get; set; }

    [ObservableProperty]
    public partial Guid? CommandId { get; set; }

    /// <summary>本单独立的明细编辑集合，切换头行不会丢弃未保存内容。</summary>
    public BaseListModel<InboundLineModel, InboundLineDetailDto> Lines { get; } = new(_ => new InboundLineModel());

    /// <summary>当前明细合计，仅用于页面显示，不是持久化字段。</summary>
    public decimal TotalQuantity => Lines.Items.Sum(x => x.Quantity);

    /// <summary>仅开放人工活动草稿编辑。</summary>
    public bool CanEdit => CreationMode == InboundCreationModeEnum.Manual && LifecycleStatus == InventoryDocumentLifecycleStatusEnum.Active && PostingStatus == InboundPostingStatusEnum.Unposted;
    /// <summary>已有来源行时禁止改类型，避免把采购行标识解释为生产行。</summary>
    public bool CanChangeDocumentType => CanEdit && Lines.Items.Count == 0;

    /// <summary>明细变化只更新显示合计，脏状态仍由头行各自维护。</summary>
    public InboundHeaderModel()
    {
        Lines.ListStateChanged += (_, _) => { OnPropertyChanged(nameof(TotalQuantity)); OnPropertyChanged(nameof(CanChangeDocumentType)); };
        PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CanEdit)) OnPropertyChanged(nameof(CanChangeDocumentType)); };
    }

    /// <inheritdoc/>
    public override string? ValidateRow() => WarehouseSiteId == Guid.Empty ? "请选择收货仓库。" : null;

    /// <inheritdoc/>
    protected override bool IsTrackingIgnoredProperty(string? name) =>
        base.IsTrackingIgnoredProperty(name) || name is nameof(TotalQuantity) or nameof(CanEdit) or nameof(CanChangeDocumentType);

    /// <inheritdoc/>
    protected override void LoadFromDto(InboundHeaderDetailDto dto) => dto.Adapt(this);

    /// <inheritdoc/>
    public override InboundHeaderDetailDto ToDto() => this.Adapt<InboundHeaderDetailDto>();
}
