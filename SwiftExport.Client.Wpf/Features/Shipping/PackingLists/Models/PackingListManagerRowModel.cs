using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Shipping.PackingLists.Models;

/// <summary>装箱单管理页只读摘要行，不承载详情编辑或保存命令。</summary>
public sealed partial class PackingListManagerRowModel : BaseLineModel<PackingListManagerListItemDto>
{
    [ObservableProperty]
    public partial string PackingListNo { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Guid CustomerId { get; set; }

    [ObservableProperty]
    public partial string CustomerNameSnapshot { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTime InvoiceDate { get; set; }

    [ObservableProperty]
    public partial string? ContainerNo { get; set; }

    [ObservableProperty]
    public partial string? SealNo { get; set; }

    [ObservableProperty]
    public partial DateTime? Etd { get; set; }

    [ObservableProperty]
    public partial DateTime? Eta { get; set; }

    [ObservableProperty]
    public partial PackingListLifecycleStatusEnum LifecycleStatus { get; set; }

    [ObservableProperty]
    public partial PackingListPostingStatusEnum PostingStatus { get; set; }

    [ObservableProperty]
    public partial string InvoiceNosSummary { get; set; } = string.Empty;

    /// <summary>接纳服务端摘要，脏状态抑制由基类加载入口负责。</summary>
    protected override void LoadFromDto(PackingListManagerListItemDto dto) => dto.Adapt(this);

    /// <summary>生成摘要快照，仅用于基类状态管理，不作为保存请求。</summary>
    public override PackingListManagerListItemDto ToDto() => this.Adapt<PackingListManagerListItemDto>();
}

