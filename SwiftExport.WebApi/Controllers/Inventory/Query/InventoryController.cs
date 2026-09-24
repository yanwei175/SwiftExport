using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Inventory.Query;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Inventory.Query;

/// <summary>提供受认证保护的库存余额、流水和 Trace 只读 HTTP 边界。</summary>
[ApiController]
[Authorize]
[Route("api/inventory")]
public sealed class InventoryController(IInventoryQueryService service) : ControllerBase
{
    /// <summary>分页查询库存余额。</summary>
    [HttpPost("balances/search")]
    public Task<ServiceResult<PagedResponseBase<InventoryBalanceDto>>> SearchBalancesAsync([FromBody] InventoryBalanceSearchRequest request, CancellationToken cancellationToken) => service.SearchBalancesAsync(request, cancellationToken);
    /// <summary>分页查询不可变库存流水。</summary>
    [HttpPost("ledger/search")]
    public Task<ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>> SearchLedgerAsync([FromBody] InventoryLedgerSearchRequest request, CancellationToken cancellationToken) => service.SearchLedgerAsync(request, cancellationToken);
    /// <summary>分页读取余额 Trace 与对账结果。</summary>
    [HttpGet("balances/{uid:guid}/trace")]
    public Task<ServiceResult<InventoryBalanceTraceDto>> GetTraceAsync(Guid uid, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) => service.GetBalanceTraceAsync(uid, pageIndex, pageSize, cancellationToken);
}
