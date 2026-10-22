using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExportClient.Mvvm;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Shipping.PackingLists.Models;

/// <summary>管理页待应用查询条件；客户按身份多选，空集合表示不限，分页从一开始。</summary>
public sealed partial class PackingListManagerSearchModel : ObservableObjectEx
{
    [ObservableProperty]
    public partial string? Keyword { get; set; }

    /// <summary>供客户控件提交的完整候选对象；请求仅提取 Uid，不按代码或当前页结果过滤。</summary>
    public ObservableCollection<CustomerDto> SelectedCustomers { get; } = [];

    /// <summary>已在控件中确认的过账状态；查询时复制，避免继续勾选污染在途请求。</summary>
    public ObservableCollection<PackingListPostingStatusEnum> PostingStatuses { get; } = [];

    [ObservableProperty]
    public partial DateTime? InvoiceDateFrom { get; set; }

    [ObservableProperty]
    public partial DateTime? InvoiceDateTo { get; set; }

    [ObservableProperty]
    public partial int PageIndex { get; set; } = 1;

    [ObservableProperty]
    public partial int PageSize { get; set; } = 20;
}

