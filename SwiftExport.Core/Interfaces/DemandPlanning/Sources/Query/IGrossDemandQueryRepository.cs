using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces.DemandPlanning.Sources.Query;

/// <summary>提供成品需求工作台、分组及详情的只读查询。</summary>
public interface IGrossDemandQueryRepository
{
    /// <summary>读取全部未删除成品需求关联的有效客户筛选候选，不受分页或当前生命周期条件限制。</summary>
    Task<List<GrossDemandCustomerOptionDto>> GetCustomerOptionsAsync(CancellationToken cancellationToken = default);
    /// <summary>按工作台筛选与分页读取成品需求；指定来源时返回完整来源行。</summary>
    Task<PagedResponseBase<GrossDemandListItemDto>> SearchWorkspaceAsync(
        GrossDemandSearchRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>按来源身份聚合活动范围内的成品需求分组。</summary>
    Task<IReadOnlyList<GrossDemandSourceGroupDto>> SearchSourceGroupsAsync(
        GrossDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>按身份读取一条未删除成品需求的详情。</summary>
    Task<GrossDemandDetailDto?> GetWorkspaceDetailAsync(Guid uid, CancellationToken cancellationToken = default);
}
