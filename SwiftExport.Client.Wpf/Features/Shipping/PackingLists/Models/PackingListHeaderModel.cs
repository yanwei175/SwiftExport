using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Shipping.PackingLists.Models;

/// <summary>装箱单头编辑模型，复用公共脏状态并展示有效行发票汇总。</summary>
public sealed partial class PackingListHeaderModel : BaseLineModel<PackingListHeaderDto>
{
    /// <summary>装箱单业务编号。</summary>
    [ObservableProperty]
    public partial string? PackingListNo { get; set; }

    /// <summary>最终客户逻辑标识。</summary>
    [ObservableProperty]
    public partial Guid CustomerId { get; set; }

    /// <summary>单据采用的客户名称快照，不随主数据更新。</summary>
    [ObservableProperty]
    public partial string? CustomerNameSnapshot { get; set; }

    /// <summary>单据采用的客户地址快照。</summary>
    [ObservableProperty]
    public partial string? CustomerAddressSnapshot { get; set; }

    /// <summary>自由填写的备注快照，不关联联系人实体。</summary>
    [ObservableProperty]
    public partial string? AttentionSnapshot { get; set; }

    /// <summary>用户自定义单据日期，不作为库存记账日期。</summary>
    [ObservableProperty]
    public partial DateTime InvoiceDate { get; set; }

    /// <summary>集装箱号。</summary>
    [ObservableProperty]
    public partial string? ContainerNo { get; set; }

    /// <summary>封条号。</summary>
    [ObservableProperty]
    public partial string? SealNo { get; set; }

    /// <summary>装运箱唛。</summary>
    [ObservableProperty]
    public partial string? ShippingMark { get; set; }

    /// <summary>单据备注。</summary>
    [ObservableProperty]
    public partial string? Note { get; set; }

    /// <summary>预计离港日期。</summary>
    [ObservableProperty]
    public partial DateTime? Etd { get; set; }

    /// <summary>预计到达日期。</summary>
    [ObservableProperty]
    public partial DateTime? Eta { get; set; }

    /// <summary>单据生命周期，与库存状态分别表达。</summary>
    [ObservableProperty]
    public partial PackingListLifecycleStatusEnum LifecycleStatus { get; set; }

    /// <summary>库存过账状态，不表示已实现过账入口。</summary>
    [ObservableProperty]
    public partial PackingListPostingStatusEnum PostingStatus { get; set; }

    /// <summary>库存业务日期，未过账可空，与单据日期独立。</summary>
    [ObservableProperty]
    public partial DateTime? PostingDate { get; set; }

    /// <summary>实际过账操作时间。</summary>
    [ObservableProperty]
    public partial DateTime? PostedDate { get; set; }

    /// <summary>过账操作人。</summary>
    [ObservableProperty]
    public partial string? PostedBy { get; set; }

    /// <summary>反向操作时间。</summary>
    [ObservableProperty]
    public partial DateTime? ReversedDate { get; set; }

    /// <summary>反向操作人。</summary>
    [ObservableProperty]
    public partial string? ReversedBy { get; set; }

    /// <summary>反向原因。</summary>
    [ObservableProperty]
    public partial string? ReverseReason { get; set; }

    /// <summary>保留建单命令身份，具体命令校验另行实现。</summary>
    [ObservableProperty]
    public partial Guid CreateCommandId { get; set; }

    /// <summary>过账命令身份，用于后续幂等处理。</summary>
    [ObservableProperty]
    public partial Guid? PostCommandId { get; set; }

    /// <summary>反向命令身份，用于后续幂等处理。</summary>
    [ObservableProperty]
    public partial Guid? ReverseCommandId { get; set; }

    /// <summary>接纳服务端字段；映射差异由集中配置负责。</summary>
    protected override void LoadFromDto(PackingListHeaderDto dto) => dto.Adapt(this);

    /// <summary>导出当前模型快照，不代表保存命令。</summary>
    public override PackingListHeaderDto ToDto() => this.Adapt<PackingListHeaderDto>();

    /// <summary>头模型拥有物理行集合，加载由调用方分别接纳头和行。</summary>
    public PackingListLineListModel Lines { get; } = new();

    /// <summary>按有效行展示去重发票号，不作为头部编辑事实。</summary>
    public string InvoiceNosSummary => Lines.InvoiceNosSummary;

    /// <summary>仅转发本对象拥有的集合通知，不创建全局订阅。</summary>
    public PackingListHeaderModel()
    {
        Lines.ListStateChanged += (_, _) => OnPropertyChanged(nameof(InvoiceNosSummary));
    }

    /// <summary>派生汇总和子集合不制造头记录脏状态。</summary>
    protected override bool IsTrackingIgnoredProperty(string? propertyName) =>
        base.IsTrackingIgnoredProperty(propertyName)
        || propertyName is nameof(InvoiceNosSummary) or nameof(Lines);
}

