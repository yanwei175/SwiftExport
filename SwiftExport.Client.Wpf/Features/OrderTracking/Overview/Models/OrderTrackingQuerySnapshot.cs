using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.SysEnums;
namespace SwiftExportClient.Features.OrderTracking.Overview.Models;
/// <summary>冻结一次已提交的业务条件；后续编辑、分页和命中定位均不能修改该快照。</summary>
public sealed partial record OrderTrackingQuerySnapshot(string Keyword, string SourceType, DateTime? RequiredDateFrom,
    DateTime? RequiredDateTo, IReadOnlyList<string> CustomerCodes, IReadOnlyList<GrossDemandLifecycleStatusEnum> LifecycleStatuses,
    IReadOnlyList<string> AttentionCodes)
{
    /// <summary>比较业务条件，集合按集合语义比较；分页不属于筛选条件。</summary>
    public bool HasSameConditions(OrderTrackingQuerySnapshot other) => Keyword == other.Keyword && SourceType == other.SourceType
        && RequiredDateFrom == other.RequiredDateFrom && RequiredDateTo == other.RequiredDateTo
        && CustomerCodes.ToHashSet(StringComparer.Ordinal).SetEquals(other.CustomerCodes)
        && LifecycleStatuses.ToHashSet().SetEquals(other.LifecycleStatuses) && AttentionCodes.ToHashSet(StringComparer.Ordinal).SetEquals(other.AttentionCodes);
    /// <summary>来源分页每次复制条件集合，不把可变传输请求用作已应用状态。</summary>
    public OrderTrackingSourceSearchRequest ForPage(int page, int size) => new()
    {
        Keyword = Keyword, SourceTypes = SourceType.Length == 0 ? [] : [SourceType], RequiredDateFrom = RequiredDateFrom, RequiredDateTo = RequiredDateTo,
        CustomerCodes = CustomerCodes.ToList(), LifecycleStatuses = LifecycleStatuses.ToList(), AttentionCodes = AttentionCodes.ToList(),
        PageIndex = Math.Max(1, page), PageSize = Math.Clamp(size, 1, 200)
    };
    /// <summary>命中请求使用同一已应用条件，刻意不携带任何分页字段。</summary>
    public OrderTrackingSourceMatchRequest ForMatches() => new()
    {
        Keyword = Keyword, SourceTypes = SourceType.Length == 0 ? [] : [SourceType], RequiredDateFrom = RequiredDateFrom, RequiredDateTo = RequiredDateTo,
        CustomerCodes = CustomerCodes.ToList(), LifecycleStatuses = LifecycleStatuses.ToList(), AttentionCodes = AttentionCodes.ToList()
    };
}
