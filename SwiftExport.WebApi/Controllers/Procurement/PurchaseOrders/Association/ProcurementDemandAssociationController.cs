using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Procurement.PurchaseOrders.Association;

/// <summary>已有采购关联物料需求的 HTTP 边界；所有选择来自专用请求，不依赖详情页状态。</summary>
[ApiController]
[Authorize]
[Route("api/procurement/orders")]
public sealed class ProcurementDemandAssociationController(IProcurementDemandAssociationService service) : ControllerBase
{
    /// <summary>读取采购关联上下文。</summary>
    [HttpGet("{uid}/demand-association-context")]
    public Task<ServiceResult<ProcurementDemandAssociationContextDto>> GetDemandAssociationContextAsync(Guid uid, CancellationToken cancellationToken = default)
        => service.GetContextAsync(uid, cancellationToken);

    /// <summary>检索用户指定采购行的物料候选。</summary>
    [HttpPost("{uid}/demand-association-candidates")]
    public Task<ServiceResult<PagedResponseBase<ProcurementDemandCandidateDto>>> SearchDemandAssociationCandidatesAsync(Guid uid, [FromBody] ProcurementDemandCandidateSearchRequest request, CancellationToken cancellationToken = default)
        => service.SearchCandidatesAsync(uid, request, cancellationToken);

    /// <summary>只读验证本次分配，不占用需求。</summary>
    [HttpPost("{uid}/demand-association-preview")]
    public Task<ServiceResult<ProcurementDemandAssociationPreviewDto>> PreviewDemandAssociationAsync(Guid uid, [FromBody] PreviewProcurementDemandAssociationRequest request, CancellationToken cancellationToken = default)
        => service.PreviewAsync(uid, request, cancellationToken);

    /// <summary>按冻结命令原子追加需求关联。</summary>
    [HttpPost("{uid}/associate-demands")]
    public Task<ServiceResult<ProcurementOrderCommandResult>> AssociateDemandsAsync(Guid uid, [FromBody] AssociateProcurementDemandsCommand command, CancellationToken cancellationToken = default)
        => service.AssociateAsync(uid, command, cancellationToken);
}
