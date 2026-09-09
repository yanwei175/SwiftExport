using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.DemandPlanning;

namespace SwiftExport.Application.Services.DemandPlanning;

/// <summary>物料需求的新服务入口，负责来源建立交接及物料查询，供后续页面能力演进。</summary>
/// <remarks>当前查询仍兼容 Allocation 和旧追溯 DTO，本次迁移不改变业务行为。</remarks>
public sealed class NetDemandServices(
    ServiceBaseContext context,
    IGrossDemandRepository grossDemandRepository,
    INetDemandRepository netDemandRepository,
    INetDemandAllocationRepository allocationRepository,
    INetDemandExecutionLinkRepository executionLinkRepository,
    IMaterialDemandEstablishmentService establishmentService) : INetDemandServices
{
    private readonly ILogger<NetDemandServices> _logger = context.LoggerFactory.CreateLogger<NetDemandServices>();

    /// <summary>查询可建立物料需求的来源候选，返回分页结果。</summary>
    public Task<ServiceResult<PagedResponseBase<SourceDemandCandidateDto>>> SearchSourceDemandCandidatesAsync(
        SourceDemandCandidateSearchRequest request,
        CancellationToken cancellationToken = default)
        => establishmentService.SearchCandidatesAsync(request, cancellationToken);

    /// <summary>查询待建立来源单分组，分组资格与物料领域候选规则保持一致。</summary>
    public async Task<ServiceResult<List<SourceDemandCandidateGroupDto>>> GetSourceDemandCandidateGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var groups = await netDemandRepository.GetSourceDemandCandidateGroupsAsync(cancellationToken);
            return ServiceResult<List<SourceDemandCandidateGroupDto>>.Ok([.. groups]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询待建立来源单分组失败。");
            return ServiceResult<List<SourceDemandCandidateGroupDto>>.Fail("查询待建立来源单失败。");
        }
    }

    /// <summary>按来源类型和来源 Uid 完整查询一张来源单的待建立行。</summary>
    public async Task<ServiceResult<List<SourceDemandCandidateDto>>> GetSourceDemandCandidatesBySourceAsync(
        string sourceType,
        Guid sourceUid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceType) || sourceUid == Guid.Empty)
            return ServiceResult<List<SourceDemandCandidateDto>>.Fail("来源类型和来源 Uid 不能为空。");
        try
        {
            var items = await netDemandRepository.GetSourceDemandCandidatesBySourceAsync(
                sourceType.Trim(), sourceUid, cancellationToken);
            return ServiceResult<List<SourceDemandCandidateDto>>.Ok([.. items]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询来源 {SourceType}/{SourceUid} 的待建立行失败。", sourceType, sourceUid);
            return ServiceResult<List<SourceDemandCandidateDto>>.Fail("查询来源单待建立明细失败。");
        }
    }

    /// <summary>按来源身份和版本建立物料需求，事务及幂等由既有建立用例负责。</summary>
    public Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishFromSourcesAsync(
        EstablishMaterialDemandsCommand command,
        CancellationToken cancellationToken = default)
        => establishmentService.EstablishAsync(command, cancellationToken);

    /// <summary>安全回退来源物料修订，保留既有历史及执行阻断。</summary>
    public Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackSourceEstablishmentAsync(
        RollbackMaterialDemandsCommand command,
        CancellationToken cancellationToken = default)
        => establishmentService.RollbackAsync(command, cancellationToken);

    /// <summary>查询物料需求列表，沿用现有筛选和数量口径。</summary>
    public async Task<ServiceResult<PagedResponseBase<NetDemandListItemDto>>> SearchAsync(
        NetDemandSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new NetDemandSearchRequest();
            request.PageIndex = Math.Max(1, request.PageIndex);
            request.PageSize = Math.Clamp(request.PageSize, 1, 200);
            return ServiceResult<PagedResponseBase<NetDemandListItemDto>>.Ok(
                await netDemandRepository.SearchWorkspaceAsync(request, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询净需求工作台失败。");
            return ServiceResult<PagedResponseBase<NetDemandListItemDto>>.Fail("查询净需求失败。");
        }
    }

    /// <summary>查询物料工作台的来源导航分组。</summary>
    public async Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchSourceGroupsAsync(
        NetDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new NetDemandSourceGroupSearchRequest();
            var groups = await netDemandRepository.SearchSourceGroupsAsync(request, cancellationToken);
            return ServiceResult<List<NetDemandSourceGroupDto>>.Ok([.. groups]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询净需求来源分组失败。");
            return ServiceResult<List<NetDemandSourceGroupDto>>.Fail("查询净需求来源分组失败。");
        }
    }

    /// <summary>读取物料详情；活动计划兼容字段随后续页面治理。</summary>
    public async Task<ServiceResult<NetDemandDetailDto>> GetDetailAsync(
        Guid uid,
        CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty) return ServiceResult<NetDemandDetailDto>.Fail("净需求 Uid 不能为空。");
        try
        {
            var summary = await netDemandRepository.GetWorkspaceItemAsync(uid, cancellationToken);
            if (summary is null) return ServiceResult<NetDemandDetailDto>.Fail("未找到活动净需求。");
            var family = await netDemandRepository.GetWorkspaceFamilyAsync(summary.RootNetDemandId, cancellationToken);
            var history = await allocationRepository.GetHistoryAsync(uid, cancellationToken);
            var active = history.SingleOrDefault(x => x.LifecycleStatus == NetDemandAllocationLifecycleStatusEnum.Active);
            if (active is null) return ServiceResult<NetDemandDetailDto>.Fail("净需求没有有效的活动计划，请联系管理员修复数据。");
            var blockers = await executionLinkRepository.GetByNetDemandIdsAsync(
                family.Select(x => x.Uid), activeOnly: true, cancellationToken);
            return ServiceResult<NetDemandDetailDto>.Ok(new NetDemandDetailDto
            {
                Summary = summary,
                ActiveAllocation = active,
                RelatedCustomerSuppliedPlans = family
                    .Where(x => x.IsCustomerSupplied && x.Uid != uid)
                    .OrderBy(x => x.LineNumber)
                    .ToList(),
                BlockingExecutionLinks = blockers.ToList()
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载净需求 {Uid} 详情失败。", uid);
            return ServiceResult<NetDemandDetailDto>.Fail("加载净需求详情失败。");
        }
    }

    /// <summary>读取物料历史与执行关联，保持原追溯查询语义。</summary>
    public async Task<ServiceResult<NetDemandTraceDto>> GetTraceAsync(
        Guid uid,
        CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty) return ServiceResult<NetDemandTraceDto>.Fail("净需求 Uid 不能为空。");
        try
        {
            var summary = await netDemandRepository.GetWorkspaceItemAsync(uid, cancellationToken);
            if (summary is null) return ServiceResult<NetDemandTraceDto>.Fail("未找到活动净需求。");
            var history = await allocationRepository.GetHistoryAsync(uid, cancellationToken);
            var links = await executionLinkRepository.GetByNetDemandIdsAsync([uid], activeOnly: false, cancellationToken);
            return ServiceResult<NetDemandTraceDto>.Ok(new NetDemandTraceDto
            {
                Summary = summary,
                AllocationHistory = history.ToList(),
                ExecutionLinks = links.ToList()
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载净需求 {Uid} 追溯失败。", uid);
            return ServiceResult<NetDemandTraceDto>.Fail("加载净需求追溯失败。");
        }
    }

    /// <summary>读取指定来源的物料与执行历史。</summary>
    public async Task<ServiceResult<GrossDemandTraceDto>> GetSourceDemandTraceAsync(
        Guid grossDemandId,
        CancellationToken cancellationToken = default)
    {
        if (grossDemandId == Guid.Empty) return ServiceResult<GrossDemandTraceDto>.Fail("来源需求 Uid 不能为空。");
        try
        {
            var detail = await grossDemandRepository.GetWorkspaceDetailAsync(grossDemandId, cancellationToken);
            if (detail is null) return ServiceResult<GrossDemandTraceDto>.Fail("未找到来源需求。");
            var demands = await netDemandRepository.GetByGrossDemandForTraceAsync(grossDemandId, cancellationToken);
            var links = await executionLinkRepository.GetTraceByNetDemandIdsAsync(demands.Select(item => item.Uid), cancellationToken);
            return ServiceResult<GrossDemandTraceDto>.Ok(new GrossDemandTraceDto
            {
                GrossDemand = detail, NetDemands = [.. demands], ExecutionLinks = [.. links]
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载来源需求 {GrossDemandId} 的物料追溯失败。", grossDemandId);
            return ServiceResult<GrossDemandTraceDto>.Fail("加载来源物料追溯失败。");
        }
    }

}
