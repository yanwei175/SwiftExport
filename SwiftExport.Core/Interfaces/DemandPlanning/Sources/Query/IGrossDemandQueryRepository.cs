using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand.Trace;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces.DemandPlanning.Sources.Query;

/// <summary>提供来源需求工作台、分组及详情的只读查询。</summary>
public interface IGrossDemandQueryRepository
{
    /// <summary>按工作台筛选与分页读取来源需求；指定来源时返回完整来源行。</summary>
    Task<PagedResponseBase<GrossDemandListItemDto>> SearchWorkspaceAsync(
        GrossDemandSearchRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>按来源身份聚合活动范围内的来源需求分组。</summary>
    Task<IReadOnlyList<GrossDemandSourceGroupDto>> SearchSourceGroupsAsync(
        GrossDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>按身份读取一条未删除来源需求的详情。</summary>
    Task<GrossDemandDetailDto?> GetWorkspaceDetailAsync(Guid uid, CancellationToken cancellationToken = default);
    /// <summary>按来源需求身份读取拆解、采购及正式交付历史证据。</summary>
    Task<GrossDemandTraceDetailDto?> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default);
}
