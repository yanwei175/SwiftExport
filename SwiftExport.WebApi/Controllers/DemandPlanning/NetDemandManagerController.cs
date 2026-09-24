using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Sources;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.SupplierSuggestion;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Establishment;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Reversal;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.DemandPlanning;

/// <summary>
/// 物料需求管理 HTTP 边界：分别转发查询、推荐供应商建议、来源建立及安全回退。
/// </summary>
[ApiController]
[Route("api/demand-planning/net-demands")]
public sealed class NetDemandManagerController(
    IMaterialDemandQueryService queryService,
    IMaterialDemandSourceQueryService sourceQueryService,
    IMaterialDemandSupplierSuggestionService suggestionService,
    IMaterialDemandEstablishmentService establishmentService,
    IMaterialDemandReversalService reversalService) : ControllerBase
{
    /// <summary>查询不依赖计划分配表的物料需求工作台列表。</summary>
    [HttpPost("material-search")]
    public Task<ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>> SearchManagerAsync(
        [FromBody] MaterialDemandManagerSearchRequest request,
        CancellationToken cancellationToken)
        => queryService.SearchManagerAsync(request, cancellationToken);

    /// <summary>查询仅按来源事实汇总的物料需求来源导航。</summary>
    [HttpPost("material-source-groups")]
    public Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchMaterialSourceGroupsAsync(
        [FromBody] MaterialDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken)
        => queryService.SearchMaterialSourceGroupsAsync(request, cancellationToken);

    /// <summary>局部保存物料需求推荐采购供应商；不修改已有采购与送达事实。</summary>
    [HttpPost("suggested-purchase-supplier")]
    [PreventDuplicateRequest(WindowSeconds = 3)]
    public Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>> UpdateSuggestedPurchaseSupplierAsync(
        [FromBody] UpdateSuggestedPurchaseSupplierCommand command,
        CancellationToken cancellationToken)
        => suggestionService.UpdateSuggestedPurchaseSupplierAsync(command, cancellationToken);

    /// <summary>查询全部仍包含待建立行的来源单分组。</summary>
    [HttpGet("source-demands/candidate-groups")]
    public Task<ServiceResult<List<SourceDemandCandidateGroupDto>>> GetSourceDemandCandidateGroupsAsync(
        CancellationToken cancellationToken)
        => sourceQueryService.GetSourceDemandCandidateGroupsAsync(cancellationToken);

    /// <summary>按稳定来源身份完整查询一张来源单的全部待建立行。</summary>
    [HttpGet("source-demands/candidates/{sourceType}/{sourceUid:guid}")]
    public Task<ServiceResult<List<SourceDemandCandidateDto>>> GetSourceDemandCandidatesBySourceAsync(
        string sourceType,
        Guid sourceUid,
        CancellationToken cancellationToken)
        => sourceQueryService.GetSourceDemandCandidatesBySourceAsync(sourceType, sourceUid, cancellationToken);

    /// <summary>从来源需求原子建立物料需求。</summary>
    [HttpPost("source-demands/establish")]
    public Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishFromSourcesAsync(
        [FromBody] EstablishMaterialDemandsCommand command,
        CancellationToken cancellationToken)
        => establishmentService.EstablishAsync(command, cancellationToken);

    /// <summary>安全回退来源对应的活动物料需求修订。</summary>
    [HttpPost("source-demands/rollback")]
    public Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackSourceEstablishmentAsync(
        [FromBody] RollbackMaterialDemandsCommand command,
        CancellationToken cancellationToken)
        => reversalService.RollbackAsync(command, cancellationToken);

}
