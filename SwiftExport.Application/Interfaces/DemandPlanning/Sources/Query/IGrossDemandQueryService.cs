using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.DemandPlanning.Sources.Query;

/// <summary>定义成品需求本体的列表、分组和详情只读用例。</summary>
public interface IGrossDemandQueryService
{
    /// <summary>读取成品需求客户候选，仅供来源查看权限下按身份筛选。</summary>
    Task<ServiceResult<List<GrossDemandCustomerOptionDto>>> GetCustomerOptionsAsync(CancellationToken cancellationToken = default);
    /// <summary>按现有筛选及分页规则查询成品需求。</summary>
    Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>> SearchAsync(GrossDemandSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>按来源单汇总导航分组。</summary>
    Task<ServiceResult<List<GrossDemandSourceGroupDto>>> SearchSourceGroupsAsync(GrossDemandSourceGroupSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>读取单条成品需求本体详情。</summary>
    Task<ServiceResult<GrossDemandDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default);
}
