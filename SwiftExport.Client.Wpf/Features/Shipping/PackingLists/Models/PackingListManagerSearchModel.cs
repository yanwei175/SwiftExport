using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Shipping.PackingLists.Models;

/// <summary>管理页查询条件；空状态表示全部，分页从一开始。</summary>
public sealed partial class PackingListManagerSearchModel : ObservableObjectEx
{
    [ObservableProperty]
    public partial string? Keyword { get; set; }

    [ObservableProperty]
    public partial Guid? CustomerId { get; set; }

    [ObservableProperty]
    public partial PackingListPostingStatusEnum? PostingStatus { get; set; }

    [ObservableProperty]
    public partial DateTime? InvoiceDateFrom { get; set; }

    [ObservableProperty]
    public partial DateTime? InvoiceDateTo { get; set; }

    [ObservableProperty]
    public partial int PageIndex { get; set; } = 1;

    [ObservableProperty]
    public partial int PageSize { get; set; } = 20;
}

