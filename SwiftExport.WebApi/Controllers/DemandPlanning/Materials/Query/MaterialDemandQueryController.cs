using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Sources;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand.Trace;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.DemandPlanning.Materials.Query;

/// <summary>
/// 转发物料需求工作台、来源导航和待建立来源候选的只读查询。
/// </summary>
[ApiController]
[Route("api/demand-planning/net-demands")]
public sealed class MaterialDemandQueryController(
    IMaterialDemandQueryService queryService,
    IMaterialDemandSourceQueryService sourceQueryService) : ControllerBase
{
    /// <summary>按物料需求 Uid 查询只读拆解和采购收货证据。</summary>
    [HttpGet("{uid:guid}/trace")]
    public Task<ServiceResult<MaterialDemandTraceDetailDto>> GetTraceAsync([FromRoute] Guid uid,
        CancellationToken cancellationToken) => queryService.GetTraceAsync(uid, cancellationToken);
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

}
