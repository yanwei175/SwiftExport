using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Inventory.Outbounds;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Inventory.Outbounds;

/// <summary>转发出库列表、详情和选品查询，不承担库存业务判断。</summary>
[ApiController]
[Authorize]
[Route("api/inventory/outbounds")]
public sealed class OutboundQueryController(IOutboundQueryService service) : ControllerBase
{
    /// <summary>按仓库分页读取正库存产品。</summary>
    [HttpPost("items/search")]
    public Task<ServiceResult<PagedResponseBase<OutboundItemCandidateDto>>> SearchItemsAsync([FromBody] OutboundItemSearchRequest request, CancellationToken token) => service.SearchItemsAsync(request, token);
    /// <summary>分页读取出库单摘要。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<OutboundManagerListItemDto>>> SearchAsync([FromBody] OutboundManagerSearchRequest request, CancellationToken token) => service.SearchAsync(request, token);
    /// <summary>读取单据头行详情。</summary>
    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<OutboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token) => service.GetAsync(uid, token);
}
