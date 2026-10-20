using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Procurement.PurchaseOrders.Reversal;

/// <summary>采购Reversal用例的 HTTP 边界；保留既有路由和授权属性。</summary>
[ApiController]
[Authorize]
[Route("api/procurement/orders")]
public sealed class ProcurementOrderReversalController(IProcurementOrderReversalService reversals) : ControllerBase
{
    /// <summary>读取整单草稿删除资格和将释放的全部占用，不写入或预留资源。</summary>
    [HttpPost("{uid}/delete-draft-preview")]
    public Task<ServiceResult<ProcurementReversalPreviewDto>> PreviewDeleteDraftAsync(Guid uid,[FromBody]ProcurementReversalPreviewRequest request,CancellationToken cancellationToken=default)
        => reversals.PreviewAsync(uid,SwiftExport.Contracts.SysEnums.ProcurementReversalOperation.DeleteDraft,request,cancellationToken);
    /// <summary>整单删除从未确认且无下游历史的待确认活动草稿。</summary>
    [HttpPost("{uid}/delete-draft")]
    public Task<ServiceResult<ProcurementOrderCommandResult>> DeleteDraftAsync(Guid uid, [FromBody] DeleteProcurementDraftCommand request, CancellationToken cancellationToken = default)
        => reversals.DeleteDraftAsync(uid, request, cancellationToken);
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
}
