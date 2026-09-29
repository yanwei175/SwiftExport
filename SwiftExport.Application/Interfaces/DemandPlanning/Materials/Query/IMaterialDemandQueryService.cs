using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand.Trace;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.DemandPlanning.Materials.Query;

/// <summary>定义物料需求工作台及来源导航的只读用例。</summary>
public interface IMaterialDemandQueryService
{
    /// <summary>查询单条物料需求的只读履约证据。</summary>
    Task<ServiceResult<MaterialDemandTraceDetailDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default);
    /// <summary>按现有分页和筛选规则查询物料事实与采购关联汇总。</summary>
    Task<ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>> SearchManagerAsync(MaterialDemandManagerSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>查询不含计划状态的物料来源导航分组。</summary>
    Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchMaterialSourceGroupsAsync(MaterialDemandSourceGroupSearchRequest request, CancellationToken cancellationToken = default);
}
