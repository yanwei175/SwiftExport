using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Inventory;

/// <summary>采购入库选源边界，无仓库过滤或有效供应商附加查询。</summary>
[ApiController]
[Authorize]
[Route("api/inventory/inbound-sources")]
public sealed class InboundSourcesController(IInboundManagerService service) : ControllerBase
{
    /// <summary>返回采购行分页快照，选择不预占数量。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<InboundSourceCandidateDto>>> SearchAsync([FromBody] InboundSourceSearchRequest request, CancellationToken token) => service.SearchSourcesAsync(request, token);
}
