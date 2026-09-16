using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.Shipping;

/// <summary>提供窄范围来源退出核验；其余尚未接入的交付查询明确不可用，不返回虚构余额。</summary>
public sealed class SourceDeliveryService(
    ServiceBaseContext context,
    IPackingListRepository packingListRepository) : ISourceDeliveryService
{
    private const string UnavailableCode = "DELIVERY_QUERY_UNAVAILABLE";
    private const string UnavailableMessage = "来源交付查询尚未接入，无法证明已交付数量、可交付余额或完成状态。";
    private readonly DbSession _session = context.Session;
    private readonly ILogger<SourceDeliveryService> _logger =
        context.LoggerFactory.CreateLogger<SourceDeliveryService>();

    /// <summary>明确返回查询不可用，不以空列表代表没有待交付要求。</summary>
    public Task<ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>> SearchAsync(
        SourceDemandDeliverySearchRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>.Fail(UnavailableCode, UnavailableMessage));
    }

    /// <summary>明确返回交付上下文不可用，不以零代替未知数量。</summary>
    public Task<ServiceResult<SourceDemandDeliveryItemDto>> GetAsync(
        Guid grossDemandId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ServiceResult<SourceDemandDeliveryItemDto>.Fail(UnavailableCode, UnavailableMessage));
    }

    /// <summary>交付事实缺位时拒绝所有额度声明，包括空集合，不能作为库存动作的放行依据。</summary>
    public Task<IReadOnlyDictionary<Guid, SourceDemandDeliveryItemDto>> VerifyForUpdateAsync(
        IReadOnlyCollection<SourceDeliveryClaim> claims, bool requireInventoryContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<IReadOnlyDictionary<Guid, SourceDemandDeliveryItemDto>>(
            new SourceDeliveryException(UnavailableCode, UnavailableMessage));
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
