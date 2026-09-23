using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Core.Interfaces.Shipping.PackingLists;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.Shipping;

/// <summary>统一提供 GrossDemand 交付查询、事务内额度复核和来源退出核验。</summary>
public sealed class SourceDeliveryService(
    ServiceBaseContext context,
    ISourceDeliveryQueryRepository queryRepository,
    IPackingListSourceExitRepository packingListRepository) : ISourceDeliveryService
{
    private const string UnavailableCode = SourceDeliveryErrorCodes.QueryUnavailable;
    private const string UnavailableMessage = "来源交付查询尚未接入，无法证明已交付数量、可交付余额或完成状态。";
    private readonly DbSession _session = context.Session;
    private readonly ILogger<SourceDeliveryService> _logger =
        context.LoggerFactory.CreateLogger<SourceDeliveryService>();

    /// <summary>查询活动且仍有正剩余量的来源；未知数据库事实明确返回不可用。</summary>
    public async Task<ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>> SearchAsync(
        SourceDemandDeliverySearchRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request is null)
                return ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>.Fail(
                    "SOURCE_DELIVERY_QUERY_INVALID", "查询条件不能为空。");
            request.PageIndex = Math.Max(1, request.PageIndex);
            request.PageSize = Math.Clamp(request.PageSize, 1, 200);
            if (!await queryRepository.IsSchemaReadyAsync(cancellationToken))
                return ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>.Fail(
                    UnavailableCode, "来源交付查询所需数据库结构尚未就绪。");
            return ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>.Ok(
                await queryRepository.SearchAsync(request, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询来源交付列表失败。");
            return ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>.Fail(UnavailableCode, UnavailableMessage);
        }
    }

    /// <summary>读取单项来源及当前正式交付净额。</summary>
    public async Task<ServiceResult<SourceDemandDeliveryItemDto>> GetAsync(
        Guid grossDemandId, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (grossDemandId == Guid.Empty)
                return ServiceResult<SourceDemandDeliveryItemDto>.Fail(
                    "SOURCE_DELIVERY_QUERY_INVALID", "来源需求标识不能为空。", nameof(grossDemandId));
            if (!await queryRepository.IsSchemaReadyAsync(cancellationToken))
                return ServiceResult<SourceDemandDeliveryItemDto>.Fail(
                    UnavailableCode, "来源交付查询所需数据库结构尚未就绪。");
            var item = await queryRepository.GetAsync(grossDemandId, cancellationToken);
            return item is null
                ? ServiceResult<SourceDemandDeliveryItemDto>.Fail(
                    SourceDeliveryErrorCodes.NotFound, "来源需求不存在或已删除。", nameof(grossDemandId))
                : ServiceResult<SourceDemandDeliveryItemDto>.Ok(item);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取来源需求 {GrossDemandId} 的交付上下文失败。", grossDemandId);
            return ServiceResult<SourceDemandDeliveryItemDto>.Fail(UnavailableCode, UnavailableMessage);
        }
    }

    /// <summary>在调用方事务内锁定来源并复核聚合额度，避免同来源多行和并发请求穿透余额。</summary>
    public async Task<IReadOnlyDictionary<Guid, SourceDemandDeliveryItemDto>> VerifyForUpdateAsync(
        IReadOnlyCollection<SourceDeliveryClaim> claims,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_session.Transaction is null)
            throw new InvalidOperationException("来源交付额度复核必须加入调用方业务事务。");
        if (claims is null)
            throw new SourceDeliveryException(SourceDeliveryErrorCodes.ClaimInvalid, "来源交付额度声明不能为空。");
        if (claims.Count == 0) return new Dictionary<Guid, SourceDemandDeliveryItemDto>();
        if (claims.Any(claim => claim.GrossDemandId == Guid.Empty || claim.Quantity <= 0
            || decimal.Round(claim.Quantity, 4) != claim.Quantity))
            throw new SourceDeliveryException(SourceDeliveryErrorCodes.ClaimInvalid, "来源交付额度声明必须具有有效来源及最多四位小数的正数量。");

        try
        {
            if (!await queryRepository.IsSchemaReadyAsync(cancellationToken))
                throw new SourceDeliveryException(UnavailableCode, "来源交付查询所需数据库结构尚未就绪。");
            var totals = claims.GroupBy(claim => claim.GrossDemandId)
                .ToDictionary(group => group.Key, group => group.Sum(claim => claim.Quantity));
            var rows = await queryRepository.GetForUpdateAsync(totals.Keys.ToArray(), cancellationToken);
            var result = rows.ToDictionary(row => row.GrossDemandId);
            foreach (var pair in totals)
            {
                if (!result.TryGetValue(pair.Key, out var row))
                    throw new SourceDeliveryException(SourceDeliveryErrorCodes.NotFound, "来源需求不存在或已删除。");
                if (!row.IsActive || !row.CustomerId.HasValue)
                    throw new SourceDeliveryException(SourceDeliveryErrorCodes.Inactive, "来源需求已关闭、作废或缺少交付客户。");
                var related = claims.Where(claim => claim.GrossDemandId == pair.Key).ToArray();
                if (related.Any(claim => claim.ExpectedDemandVersion.HasValue
                    && claim.ExpectedDemandVersion.Value != row.DemandVersion)
                    || related.Any(claim => claim.ExpectedSourceVersion.HasValue
                        && claim.ExpectedSourceVersion.Value != row.SourceVersion))
                    throw new SourceDeliveryException(SourceDeliveryErrorCodes.VersionConflict, "来源需求或上游事实版本已变化，请刷新后重试。");
                if (pair.Value > row.RemainingQty)
                    throw new SourceDeliveryException(SourceDeliveryErrorCodes.Exceeded, "申报数量超过来源当前剩余可交付量。");
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (SourceDeliveryException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "复核来源交付额度失败。");
            throw new SourceDeliveryException(UnavailableCode, UnavailableMessage, ex);
        }
    }

    /// <summary>缺少正式交付证据时拒绝结案证明，不以来源状态或调用方数量推断完成。</summary>
    public Task VerifyClosureAsync(string sourceType, Guid sourceUid,
        IReadOnlyDictionary<Guid, decimal> expectedLines, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException(new SourceDeliveryException(UnavailableCode, UnavailableMessage));
    }

    /// <summary>
    /// 在来源调用方事务内核验有效 PackingList 草稿、已过账交付及正式客户出库均已退出。
    /// </summary>
    public async Task VerifySourceExitAsync(
        string sourceType,
        Guid sourceUid,
        IReadOnlyCollection<Guid> grossDemandIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_session.Transaction is null)
            throw new InvalidOperationException("来源交付退出核验必须加入调用方业务事务。");
        if (string.IsNullOrWhiteSpace(sourceType) || sourceUid == Guid.Empty
            || grossDemandIds.Count == 0 || grossDemandIds.Any(id => id == Guid.Empty))
            throw new SourceDeliveryException("SOURCE_DELIVERY_HISTORY_INVALID", "来源交付退出身份不完整，不能证明退出安全。");

        try
        {
            if (!await packingListRepository.IsSchemaReadyAsync(cancellationToken))
                throw new SourceDeliveryException(UnavailableCode, "来源交付核验所需数据库结构尚未就绪。");
            var assessment = await packingListRepository.AssessSourceExitForUpdateAsync(
                grossDemandIds, cancellationToken);
            if (assessment.InvalidHistoryCount > 0)
                throw new SourceDeliveryException("SOURCE_DELIVERY_HISTORY_INVALID",
                    "来源关联的 PackingList 或正式客户出库历史不完整，不能自动退出。");
            if (assessment.HasEffectiveDelivery)
                throw new SourceDeliveryException("SOURCE_DELIVERY_EXISTS",
                    "来源仍被 PackingList 草稿或未反向的正式客户交付引用，请先完成对应反向处理。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SourceDeliveryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "核验来源 {SourceType}/{SourceUid} 的交付退出事实失败。", sourceType, sourceUid);
            throw new SourceDeliveryException(UnavailableCode,
                "来源交付查询不可用，无法证明来源退出安全。", ex);
        }
    }
}
