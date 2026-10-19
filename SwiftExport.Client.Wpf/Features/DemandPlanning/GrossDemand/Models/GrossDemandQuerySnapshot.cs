using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
namespace SwiftExportClient.Features.DemandPlanning.GrossDemand.Models;
/// <summary>冻结一次已应用的成品需求查询；来源模式只保留稳定来源和来源生命周期条件。</summary>
public sealed partial record GrossDemandQuerySnapshot(string Keyword, string[] SourceTypes, string[] SourceNos, string[] Skus,
    string[] CustomerSkus, Guid[] CustomerIds, GrossDemandLifecycleStatusEnum[] LifecycleStatuses,
    DateTime? RequiredDateFrom, DateTime? RequiredDateTo, bool? IsNonStandard, Guid? SourceUid = null)
{
    /// <summary>每次请求深复制集合，分页和刷新不能读取尚未应用的输入。</summary>
    public GrossDemandSearchRequest ForPage(int page, int size) => new()
    {
        Keyword = Keyword, SourceTypes = SourceTypes.ToList(), SourceNos = SourceNos.ToList(), Skus = Skus.ToList(), CustomerSkus = CustomerSkus.ToList(),
        CustomerIds = CustomerIds.ToList(), LifecycleStatuses = LifecycleStatuses.ToList(), RequiredDateFrom = RequiredDateFrom, RequiredDateTo = RequiredDateTo,
        IsNonStandard = IsNonStandard, SourceUid = SourceUid, PageIndex = SourceUid.HasValue ? 1 : Math.Max(1, page), PageSize = size
    };
    /// <summary>按集合语义比较编辑条件与已应用条件，避免多选顺序改变造成误提示。</summary>
    public bool SameConditions(GrossDemandQuerySnapshot other) => Keyword == other.Keyword && SourceUid == other.SourceUid
        && RequiredDateFrom == other.RequiredDateFrom && RequiredDateTo == other.RequiredDateTo && IsNonStandard == other.IsNonStandard
        && SourceTypes.ToHashSet().SetEquals(other.SourceTypes) && SourceNos.ToHashSet().SetEquals(other.SourceNos) && Skus.ToHashSet().SetEquals(other.Skus)
        && CustomerSkus.ToHashSet().SetEquals(other.CustomerSkus) && CustomerIds.ToHashSet().SetEquals(other.CustomerIds) && LifecycleStatuses.ToHashSet().SetEquals(other.LifecycleStatuses);
}
