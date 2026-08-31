using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.WebApi.Controllers.Procurement;
/// <summary>独立采购 HTTP 边界；操作授权、事务和幂等由 Application 校验。</summary>
[ApiController]
[Authorize]
[Route("api/procurement/orders")]
public sealed class ProcurementOrderController(IProcurementOrderQueryService queries, IProcurementOrderCommandService commands,
    IProcurementConfirmationQueryService snapshots) : ControllerBase
{
    /// <summary>单独转发已冻结确认依据读取，管理详情不包含大树。</summary>
    [HttpGet("{uid}/confirmation-snapshot")]
    public Task<ServiceResult<ProcurementConfirmationSnapshotDto>> GetConfirmationSnapshotAsync(Guid uid, CancellationToken cancellationToken = default)
        => snapshots.GetSnapshotAsync(uid, cancellationToken);
    /// <summary>转发采购SearchAsync用例，透传取消令牌。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<ProcurementOrderListItemDto>>> SearchAsync(ProcurementOrderSearchRequest request, CancellationToken cancellationToken = default)
        => queries.SearchAsync(request, cancellationToken);
    /// <summary>转发采购GetDetailAsync用例，透传取消令牌。</summary>
    [HttpGet("{uid}")]
    public Task<ServiceResult<ProcurementOrderDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
        => queries.GetDetailAsync(uid, cancellationToken);
    /// <summary>转发采购GetConfirmationContextAsync用例，透传取消令牌。</summary>
    [HttpGet("{uid}/confirmation-context")]
    public Task<ServiceResult<ProcurementConfirmationContextDto>> GetConfirmationContextAsync(Guid uid, CancellationToken cancellationToken = default)
        => queries.GetConfirmationContextAsync(uid, cancellationToken);
    /// <summary>转发采购CreateAsync用例，透传取消令牌。</summary>
    [HttpPost]
    public Task<ServiceResult<ProcurementOrderCommandResult>> CreateAsync([FromBody] CreateIndependentPurchaseOrderCommand command, CancellationToken cancellationToken = default)
        => commands.CreateAsync(command, cancellationToken);
    /// <summary>转发采购SaveAsync用例，透传取消令牌。</summary>
    [HttpPut("{uid}/commercial")]
    public Task<ServiceResult<ProcurementOrderCommandResult>> SaveAsync(Guid uid, [FromBody] SaveProcurementOrderCommand command, CancellationToken cancellationToken = default)
        => commands.SaveAsync(uid, command, cancellationToken);
    /// <summary>转发采购ConfirmAsync用例，透传取消令牌。</summary>
    [HttpPost("{uid}/confirm")]
    public Task<ServiceResult<ProcurementOrderCommandResult>> ConfirmAsync(Guid uid, [FromBody] ConfirmProcurementOrderCommand command, CancellationToken cancellationToken = default)
        => commands.ConfirmAsync(uid, command, cancellationToken);
}
