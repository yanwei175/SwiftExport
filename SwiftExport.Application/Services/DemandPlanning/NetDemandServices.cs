using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.DemandPlanning;

/// <summary>物料需求服务入口，负责来源建立交接及物料查询。</summary>
/// <remarks>工作台新查询不依赖 Allocation；详情与旧查询仍为未迁移消费者保留兼容。</remarks>
public sealed class NetDemandServices(
    ServiceBaseContext context,
    ISupplierRepository supplierRepository,
    INetDemandRepository netDemandRepository,
    INetDemandAllocationRepository allocationRepository,
    INetDemandExecutionLinkRepository executionLinkRepository,
    IMaterialDemandEstablishmentService establishmentService) : INetDemandServices
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<NetDemandServices> _logger = context.LoggerFactory.CreateLogger<NetDemandServices>();

    /// <summary>查询物料需求工作台的物料事实和有效采购关联汇总。</summary>
    public async Task<ServiceResult<PagedResponseBase<MaterialDemandWorkspaceItemDto>>> SearchMaterialWorkspaceAsync(
        MaterialDemandWorkspaceSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new MaterialDemandWorkspaceSearchRequest();
            request.PageIndex = Math.Max(1, request.PageIndex);
            request.PageSize = Math.Clamp(request.PageSize, 1, 200);
            return ServiceResult<PagedResponseBase<MaterialDemandWorkspaceItemDto>>.Ok(
                await netDemandRepository.SearchMaterialWorkspaceAsync(request, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询物料需求工作台失败。");
            return ServiceResult<PagedResponseBase<MaterialDemandWorkspaceItemDto>>.Fail("查询物料需求失败。");
        }
    }

    /// <summary>查询不含计划状态的物料需求来源导航。</summary>
    public async Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchMaterialSourceGroupsAsync(
        MaterialDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new MaterialDemandSourceGroupSearchRequest();
            var groups = await netDemandRepository.SearchMaterialSourceGroupsAsync(request, cancellationToken);
            return ServiceResult<List<NetDemandSourceGroupDto>>.Ok([.. groups]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询物料需求来源导航失败。");
            return ServiceResult<List<NetDemandSourceGroupDto>>.Fail("查询物料需求来源导航失败。");
        }
    }

    /// <summary>
    /// 以物料需求版本为并发边界局部保存推荐采购供应商；该建议不修改任何既有采购单或送达事实。
    /// </summary>
    public async Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>> UpdateSuggestedPurchaseSupplierAsync(
        UpdateSuggestedPurchaseSupplierCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command is null || command.NetDemandId == Guid.Empty || command.Version <= 0)
            return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Fail("INVALID_COMMAND", "物料需求和版本不能为空。");
        if (command.SuggestedPurchaseSupplierId == Guid.Empty)
            return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Fail("INVALID_SUPPLIER", "供应商 Uid 无效；清空建议请提交 null。");
        if (_session.Transaction is not null)
            return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Fail("TRANSACTION_ALREADY_ACTIVE", "当前请求已存在未完成事务。");

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var family = await netDemandRepository.GetActiveFamilyForUpdateAsync(command.NetDemandId);
            var demand = family.SingleOrDefault(item => item.Uid == command.NetDemandId)
                ?? throw new SuggestedSupplierException("MATERIAL_DEMAND_NOT_ACTIVE", "物料需求不存在、已回退或已作废。");
            if (demand.Version != command.Version)
                throw new SuggestedSupplierException("CONCURRENCY_CONFLICT", "物料需求已被其他用户修改，请刷新后重试。");

            Supplier? supplier = null;
            if (command.SuggestedPurchaseSupplierId.HasValue)
            {
                supplier = await supplierRepository.GetByIdAsync(command.SuggestedPurchaseSupplierId.Value, withUpdLock: true)
                    ?? throw new SuggestedSupplierException("INVALID_SUPPLIER", "推荐采购供应商不存在或已停用。");
            }
            cancellationToken.ThrowIfCancellationRequested();

            if (demand.SuggestedPurchaseSupplierId == command.SuggestedPurchaseSupplierId)
            {
                _session.Transaction.Commit();
                return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Ok(new UpdateSuggestedPurchaseSupplierResult
                {
                    NetDemandId = demand.Uid,
                    Version = demand.Version,
                    SuggestedPurchaseSupplierId = supplier?.Uid,
                    SuggestedPurchaseSupplierCode = supplier?.SupplierCode ?? string.Empty,
                    SuggestedPurchaseSupplierName = supplier?.SupplierName ?? string.Empty
                });
            }

            var affected = await netDemandRepository.UpdateSuggestedPurchaseSupplierAsync(
                demand.Uid, demand.Version, command.SuggestedPurchaseSupplierId);
            if (affected != 1)
                throw new SuggestedSupplierException("CONCURRENCY_CONFLICT", "物料需求已被其他用户修改，请刷新后重试。");
            cancellationToken.ThrowIfCancellationRequested();

            _session.Transaction.Commit();
            return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Ok(new UpdateSuggestedPurchaseSupplierResult
            {
                NetDemandId = demand.Uid,
                Version = demand.Version + 1,
                SuggestedPurchaseSupplierId = supplier?.Uid,
                SuggestedPurchaseSupplierCode = supplier?.SupplierCode ?? string.Empty,
                SuggestedPurchaseSupplierName = supplier?.SupplierName ?? string.Empty
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _session.Transaction.Rollback();
            throw;
        }
        catch (SuggestedSupplierException ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            _logger.LogError(ex, "更新物料需求 {NetDemandId} 的推荐采购供应商失败。", command.NetDemandId);
            return ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Fail("更新推荐采购供应商失败。");
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

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

    /// <summary>兼容旧计划与采购消费者的列表查询。</summary>
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
            var active = await allocationRepository.GetActiveAsync(uid, cancellationToken);
            if (active is null) return ServiceResult<NetDemandDetailDto>.Fail("净需求没有有效的活动计划，请联系管理员修复数据。");
            var blockers = await executionLinkRepository.GetActiveByNetDemandIdsAsync(
                family.Select(x => x.Uid), cancellationToken);
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

    /// <summary>表示推荐供应商局部更新可预期的业务失败。</summary>
    private sealed class SuggestedSupplierException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

}
