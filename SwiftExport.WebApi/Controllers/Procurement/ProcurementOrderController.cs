using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Procurement;
/// <summary>独立采购 HTTP 边界；操作授权、事务和幂等由 Application 校验。</summary>
[ApiController]
[Authorize]
[Route("api/procurement/orders")]
public sealed class ProcurementOrderController(IProcurementOrderQueryService queries, IProcurementOrderCommandService commands,
    IProcurementConfirmationQueryService snapshots, IProcurementOrderReversalService reversals) : ControllerBase
{
    /// <summary>预览取消确认资格，保留物料需求引用。</summary>
    [HttpPost("{uid}/cancel-confirmation-preview")]
    public Task<ServiceResult<ProcurementReversalPreviewDto>> PreviewCancelConfirmationAsync(Guid uid, [FromBody] ProcurementReversalPreviewRequest request, CancellationToken cancellationToken = default)
        => reversals.PreviewAsync(uid, SwiftExport.Contracts.SysEnums.ProcurementReversalOperation.CancelConfirmation, request, cancellationToken);

    /// <summary>取消确认回到草稿，原确认依据保留。</summary>
    [HttpPost("{uid}/cancel-confirmation")]
    public Task<ServiceResult<ProcurementOrderCommandResult>> CancelConfirmationAsync(Guid uid, [FromBody] CancelProcurementOrderConfirmationCommand request, CancellationToken cancellationToken = default)
        => reversals.CancelConfirmationAsync(uid, request, cancellationToken);

    /// <summary>预览整单作废及需求释放影响。</summary>
    [HttpPost("{uid}/void-preview")]
    public Task<ServiceResult<ProcurementReversalPreviewDto>> PreviewVoidAsync(Guid uid, [FromBody] ProcurementReversalPreviewRequest request, CancellationToken cancellationToken = default)
        => reversals.PreviewAsync(uid, SwiftExport.Contracts.SysEnums.ProcurementReversalOperation.Void, request, cancellationToken);

    /// <summary>作废整单并解除本单活动需求引用。</summary>
    [HttpPost("{uid}/void")]
    public Task<ServiceResult<ProcurementOrderCommandResult>> VoidAsync(Guid uid, [FromBody] VoidProcurementOrderCommand request, CancellationToken cancellationToken = default)
        => reversals.VoidAsync(uid, request, cancellationToken);

    /// <summary>预览删除选定草稿行及需求释放影响。</summary>
    [HttpPost("{uid}/delete-lines-preview")]
    public Task<ServiceResult<ProcurementReversalPreviewDto>> PreviewDeleteLinesAsync(Guid uid, [FromBody] ProcurementReversalPreviewRequest request, CancellationToken cancellationToken = default)
        => reversals.PreviewAsync(uid, SwiftExport.Contracts.SysEnums.ProcurementReversalOperation.DeleteLines, request, cancellationToken);

    /// <summary>立即软删除选定草稿行，不接收其他商务修改。</summary>
    [HttpPost("{uid}/delete-lines")]
    public Task<ServiceResult<ProcurementOrderCommandResult>> DeleteLinesAsync(Guid uid, [FromBody] DeleteProcurementDraftLinesCommand request, CancellationToken cancellationToken = default)
        => reversals.DeleteLinesAsync(uid, request, cancellationToken);

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
    public Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
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
    public Task<ServiceResult<ProcurementOrderCommandResult>> SaveAsync(Guid uid, [FromBody] UpdatePurchaseOrderDraftCommand command, CancellationToken cancellationToken = default)
        => commands.SaveAsync(uid, command, cancellationToken);

    /// <summary>转发采购ConfirmAsync用例，透传取消令牌。</summary>
    [HttpPost("{uid}/confirm")]
    public Task<ServiceResult<ProcurementOrderCommandResult>> ConfirmAsync(Guid uid, [FromBody] ConfirmProcurementOrderCommand command, CancellationToken cancellationToken = default)
        => commands.ConfirmAsync(uid, command, cancellationToken);

}
