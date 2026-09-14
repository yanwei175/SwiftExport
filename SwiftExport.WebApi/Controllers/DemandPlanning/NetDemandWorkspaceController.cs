using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.DemandPlanning;

/// <summary>
/// 物料需求 HTTP 边界：物料用例转发新服务，旧计划和采购路由暂留兼容。
/// </summary>
[ApiController]
[Route("api/demand-planning/net-demands")]
public sealed class NetDemandWorkspaceController(INetDemandServices service, INetDemandWorkspaceService legacyService) : ControllerBase
{
    /// <summary>查询不依赖计划分配表的物料需求工作台列表。</summary>
    [HttpPost("material-search")]
    public Task<ServiceResult<PagedResponseBase<MaterialDemandWorkspaceItemDto>>> SearchMaterialWorkspaceAsync(
        [FromBody] MaterialDemandWorkspaceSearchRequest request,
        CancellationToken cancellationToken)
        => service.SearchMaterialWorkspaceAsync(request, cancellationToken);

    /// <summary>查询仅按来源事实汇总的物料需求来源导航。</summary>
    [HttpPost("material-source-groups")]
    public Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchMaterialSourceGroupsAsync(
        [FromBody] MaterialDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken)
        => service.SearchMaterialSourceGroupsAsync(request, cancellationToken);

    /// <summary>局部保存物料需求推荐采购供应商；不修改已有采购与送达事实。</summary>
    [HttpPost("suggested-purchase-supplier")]
    [PreventDuplicateRequest(WindowSeconds = 3)]
    public Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>> UpdateSuggestedPurchaseSupplierAsync(
        [FromBody] UpdateSuggestedPurchaseSupplierCommand command,
        CancellationToken cancellationToken)
        => service.UpdateSuggestedPurchaseSupplierAsync(command, cancellationToken);

    /// <summary>查询由物料领域计算的来源需求建立候选。</summary>
    [HttpPost("source-demands/candidates/search")]
    public Task<ServiceResult<PagedResponseBase<SourceDemandCandidateDto>>> SearchSourceDemandCandidatesAsync(
        [FromBody] SourceDemandCandidateSearchRequest request,
        CancellationToken cancellationToken)
        => service.SearchSourceDemandCandidatesAsync(request, cancellationToken);

    /// <summary>查询全部仍包含待建立行的来源单分组。</summary>
    [HttpGet("source-demands/candidate-groups")]
    public Task<ServiceResult<List<SourceDemandCandidateGroupDto>>> GetSourceDemandCandidateGroupsAsync(
        CancellationToken cancellationToken)
        => service.GetSourceDemandCandidateGroupsAsync(cancellationToken);

    /// <summary>按稳定来源身份完整查询一张来源单的全部待建立行。</summary>
    [HttpGet("source-demands/candidates/{sourceType}/{sourceUid:guid}")]
    public Task<ServiceResult<List<SourceDemandCandidateDto>>> GetSourceDemandCandidatesBySourceAsync(
        string sourceType,
        Guid sourceUid,
        CancellationToken cancellationToken)
        => service.GetSourceDemandCandidatesBySourceAsync(sourceType, sourceUid, cancellationToken);

    /// <summary>从来源需求原子建立物料需求。</summary>
    [HttpPost("source-demands/establish")]
    public Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishFromSourcesAsync(
        [FromBody] EstablishMaterialDemandsCommand command,
        CancellationToken cancellationToken)
        => service.EstablishFromSourcesAsync(command, cancellationToken);

    /// <summary>安全回退来源对应的活动物料需求修订。</summary>
    [HttpPost("source-demands/rollback")]
    public Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackSourceEstablishmentAsync(
        [FromBody] RollbackMaterialDemandsCommand command,
        CancellationToken cancellationToken)
        => service.RollbackSourceEstablishmentAsync(command, cancellationToken);

    /// <summary>兼容旧计划与采购消费者的列表查询。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<NetDemandListItemDto>>> SearchAsync(
        [FromBody] NetDemandSearchRequest request,
        CancellationToken cancellationToken)
        => service.SearchAsync(request, cancellationToken);

    /// <summary>
    /// 查询净需求左侧来源导航分组。
    /// </summary>
    [HttpPost("source-groups")]
    public Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchSourceGroupsAsync(
        [FromBody] NetDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken)
        => service.SearchSourceGroupsAsync(request, cancellationToken);

    /// <summary>读取物料详情；活动计划兼容字段随后续页面治理。</summary>
    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<NetDemandDetailDto>> GetDetailAsync(
        Guid uid,
        CancellationToken cancellationToken)
        => service.GetDetailAsync(uid, cancellationToken);

    /// <summary>待废弃：旧 Allocation 替代命令，独立计划退出后移除。</summary>
    [HttpPost("plans/replace")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<NetDemandPlanCommandResult>> ReplacePlanAsync(
        [FromBody] ReplaceNetDemandPlanCommand command)
        => legacyService.ReplacePlanAsync(command);

    /// <summary>待废弃：旧计划应用默认供应商，实际供应商选择归采购。</summary>
    [HttpPost("plans/apply-product-default")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<NetDemandPlanCommandResult>> ApplyDefaultSupplierAsync(
        [FromBody] ApplyProductDefaultSupplierCommand command)
        => legacyService.ApplyDefaultSupplierAsync(command);

    /// <summary>待废弃：旧计划批量分配供应商，实际供应商选择归采购。</summary>
    [HttpPost("plans/batch-assign-supplier")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<NetDemandPlanCommandResult>> BatchAssignSupplierAsync(
        [FromBody] BatchAssignSupplierCommand command)
        => legacyService.BatchAssignSupplierAsync(command);

}
