using CommunityToolkit.Mvvm.ComponentModel;
namespace SwiftExportClient.Features.Shipping.PackingLists.Models;

/// <summary>待发货选源分页条件，客户由详情传入，不通过采购或物料分配筛选。</summary>
public sealed partial class PackingListSourcePickerSearchModel : ObservableObject
{
    /// <summary>固定最终客户，查询前必须指定。</summary>
    [ObservableProperty] public partial Guid CustomerId { get; set; }
    /// <summary>来源单号、SKU及客户型号关键词。</summary>
    [ObservableProperty] public partial string? Keyword { get; set; }
    /// <summary>从1开始的页码。</summary>
    [ObservableProperty] public partial int PageIndex { get; set; } = 1;
    /// <summary>每页记录数。</summary>
    [ObservableProperty] public partial int PageSize { get; set; } = 20;
}
