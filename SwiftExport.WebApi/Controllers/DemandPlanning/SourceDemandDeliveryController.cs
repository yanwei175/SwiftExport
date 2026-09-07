using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.DemandPlanning;

/// <summary>暴露来源需求统一可交付只读边界；来源接收仍仅供单体内上游领域调用。</summary>
[ApiController]
[Route("api/demand-planning/source-demands/delivery")]
public sealed class SourceDemandDeliveryController(ISourceDemandDeliveryService service) : ControllerBase
{
    /// <summary>按来源、客户和产品条件查询当前可交付需求。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>> SearchAsync(
        [FromBody] SourceDemandDeliverySearchRequest request,
        CancellationToken cancellationToken)
        => service.SearchAsync(request, cancellationToken);

    /// <summary>读取单项来源需求的交付上下文和库存动作阻断原因。</summary>
    [HttpGet("{grossDemandId:guid}")]
    public Task<ServiceResult<SourceDemandDeliveryItemDto>> GetAsync(
        [FromRoute] Guid grossDemandId,
        CancellationToken cancellationToken)
        => service.GetAsync(grossDemandId, cancellationToken);
}
