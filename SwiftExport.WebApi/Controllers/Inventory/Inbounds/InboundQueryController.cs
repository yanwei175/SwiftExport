using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Inventory.Inbounds;

/// <summary>入库单据查询的 HTTP 资源边界。</summary>
[ApiController]
[Authorize]
[Route("api/inventory/inbounds")]
public sealed class InboundQueryController(IInboundQueryService query) : ControllerBase
{
    /// <summary>转发分页查询。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<InboundManagerListItemDto>>> SearchAsync([FromBody] InboundManagerSearchRequest request, CancellationToken token) => query.SearchAsync(request, token);
    /// <summary>读取单据及明细。</summary>
    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<InboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token) => query.GetAsync(uid, token);
}
