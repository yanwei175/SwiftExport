using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Query;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.DemandPlanning.Sources.Query;

/// <summary>将成品需求工作台的列表、分组和详情查询转发给只读服务。</summary>
[ApiController]
[Route("api/demand-planning/gross-demands")]
public sealed class GrossDemandQueryController(IGrossDemandQueryService queryService) : ControllerBase
{
    /// <summary>在成品需求查看权限下提供客户筛选候选，避免要求客户管理权限。</summary>
    [HttpGet("customer-options")]
    public Task<ServiceResult<List<GrossDemandCustomerOptionDto>>> GetCustomerOptionsAsync(CancellationToken cancellationToken)
        =>queryService.GetCustomerOptionsAsync(cancellationToken);

    /// <summary>按工作台条件查询成品需求，保留原分页和绑定契约。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>> SearchAsync(
        [FromBody] GrossDemandSearchRequest request, CancellationToken cancellationToken)
        => queryService.SearchAsync(request, cancellationToken);

    /// <summary>按来源身份查询需求分组。</summary>
    [HttpPost("source-groups/search")]
    public Task<ServiceResult<List<GrossDemandSourceGroupDto>>> SearchSourceGroupsAsync(
        [FromBody] GrossDemandSourceGroupSearchRequest request, CancellationToken cancellationToken)
        => queryService.SearchSourceGroupsAsync(request, cancellationToken);

    /// <summary>按身份查询成品需求详情。</summary>
    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<GrossDemandDetailDto>> GetDetailAsync([FromRoute] Guid uid, CancellationToken cancellationToken)
        => queryService.GetDetailAsync(uid, cancellationToken);

}
