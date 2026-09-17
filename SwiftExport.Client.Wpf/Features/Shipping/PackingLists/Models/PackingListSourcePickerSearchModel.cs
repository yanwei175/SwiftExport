using CommunityToolkit.Mvvm.ComponentModel;
namespace SwiftExportClient.Features.Shipping.PackingLists.Models;

/// <summary>待发货选源分页条件，客户由详情传入，后端按活动物料及有效 Target 筛选。</summary>
public sealed partial class PackingListSourcePickerSearchModel : ObservableObject
{
    /// <summary>固定最终客户，查询前必须指定。</summary>
    [ObservableProperty] public partial Guid CustomerId { get; set; }
    /// <summary>来源单号、SKU及客户型号关键词。</summary>
    [ObservableProperty] public partial string? Keyword { get; set; }
    /// <summary>换行分隔的批量关键词，仅点击查询时提交。</summary>
    [ObservableProperty] public partial string? KeywordsText { get; set; }
    /// <summary>去空白、去空行及去重后的批量搜索词；不拆分型号内部空格。</summary>
    public List<string> Keywords => (KeywordsText ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    /// <summary>从1开始的页码。</summary>
    [ObservableProperty] public partial int PageIndex { get; set; } = 1;
    /// <summary>每页记录数。</summary>
    [ObservableProperty] public partial int PageSize { get; set; } = 20;
}
