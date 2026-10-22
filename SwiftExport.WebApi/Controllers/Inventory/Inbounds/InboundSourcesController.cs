using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Inventory.Inbounds;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.DTOs.Inventory;

namespace SwiftExport.WebApi.Controllers.Inventory.Inbounds;

/// <summary>采购入库选源与供应商筛选的只读边界，不预占收货数量。</summary>
[ApiController]
[Authorize]
[Route("api/inventory/inbound-sources")]
public sealed class InboundSourcesController(IInboundSourceQueryService service) : ControllerBase
{
    /// <summary>按入库查看权限分页读取供应商筛选项，不依赖主数据查看权限。</summary>
    [HttpGet("suppliers/picker")]
    public Task<ServiceResult<PagedResponseBase<InventorySupplierLookupDto>>> GetSupplierOptionsAsync([FromQuery] PagedRequestBase request, CancellationToken token) => service.GetSupplierOptionsAsync(request, token);

    /// <summary>返回采购行分页快照，选择不预占数量。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<InboundSourceCandidateDto>>> SearchAsync([FromBody] InboundSourceSearchRequest request, CancellationToken token) => service.SearchSourcesAsync(request, token);
}
