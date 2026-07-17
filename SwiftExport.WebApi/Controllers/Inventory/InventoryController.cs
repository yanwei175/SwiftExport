using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Inventory;

[ApiController]
[Route("api/inventory")]
public sealed class InventoryController(IInventoryService service) : ControllerBase
{
    [HttpPost("balances/search")]
    public Task<ServiceResult<PagedResponseBase<InventoryBalanceDto>>> SearchBalancesAsync([FromBody] InventoryBalanceSearchRequest request, CancellationToken cancellationToken) => service.SearchBalancesAsync(request, cancellationToken);
    [HttpPost("ledger/search")]
    public Task<ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>> SearchLedgerAsync([FromBody] InventoryLedgerSearchRequest request, CancellationToken cancellationToken) => service.SearchLedgerAsync(request, cancellationToken);
    [HttpGet("balances/{uid:guid}/trace")]
    public Task<ServiceResult<InventoryBalanceTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken) => service.GetBalanceTraceAsync(uid, cancellationToken);
    [HttpPost("opening-balances/preview")]
    public Task<ServiceResult<List<InventoryPostingResultDto>>> PreviewOpeningBalancesAsync([FromBody] OpeningBalanceCommand command, CancellationToken cancellationToken) => service.PreviewOpeningBalancesAsync(command, cancellationToken);
    [HttpPost("opening-balances/post")]
    public Task<ServiceResult<List<InventoryPostingResultDto>>> PostOpeningBalancesAsync([FromBody] OpeningBalanceCommand command, CancellationToken cancellationToken) => service.PostOpeningBalancesAsync(command, cancellationToken);
}
