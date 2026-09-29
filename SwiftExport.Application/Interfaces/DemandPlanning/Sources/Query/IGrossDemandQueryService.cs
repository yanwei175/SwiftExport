using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand.Trace;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.DemandPlanning.Sources.Query;

/// <summary>定义来源需求本体的列表、分组和详情只读用例。</summary>
public interface IGrossDemandQueryService
{
    /// <summary>按现有筛选及分页规则查询来源需求。</summary>
    Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>> SearchAsync(GrossDemandSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>按来源单汇总导航分组。</summary>
    Task<ServiceResult<List<GrossDemandSourceGroupDto>>> SearchSourceGroupsAsync(GrossDemandSourceGroupSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>读取单条来源需求本体详情。</summary>
    Task<ServiceResult<GrossDemandDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default);
    /// <summary>读取来源需求的只读链路追踪。</summary>
    Task<ServiceResult<GrossDemandTraceDetailDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default);
}
