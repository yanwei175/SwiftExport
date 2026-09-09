using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.Shipping;

/// <summary>统一编排正式交付查询、事务内额度复核及结案证据，不把进度或完成率写回来源需求。</summary>
public sealed class SourceDeliveryService(
    ServiceBaseContext context,
    ISourceDeliveryRepository repository) : ISourceDeliveryService
{
    private readonly DbSession session = context.Session;
    private readonly ILogger<SourceDeliveryService> logger = context.LoggerFactory.CreateLogger<SourceDeliveryService>();

    /// <inheritdoc />
    public async Task<ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>> SearchAsync(
        SourceDemandDeliverySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new SourceDemandDeliverySearchRequest();
            ValidateSourceType(request.SourceType);
            return ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>.Ok(
                await repository.SearchAsync(request, cancellationToken));
        }
        catch (SourceDeliveryException ex)
        {
            return ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "查询来源要求可交付列表失败。");
            return ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>.Fail("DELIVERY_QUERY_FAILED", "查询来源要求可交付列表失败。");
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<SourceDemandDeliveryItemDto>> GetAsync(
        Guid grossDemandId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (grossDemandId == Guid.Empty)
                throw new SourceDeliveryException("INVALID_SOURCE_DEMAND", "来源需求标识不能为空。");
            var item = await repository.GetAsync(grossDemandId, cancellationToken);
            return item is null
                ? ServiceResult<SourceDemandDeliveryItemDto>.Fail("SOURCE_DEMAND_NOT_FOUND", "来源需求不存在。")
                : ServiceResult<SourceDemandDeliveryItemDto>.Ok(item);
        }
        catch (SourceDeliveryException ex)
        {
            return ServiceResult<SourceDemandDeliveryItemDto>.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "读取来源需求 {GrossDemandId} 交付上下文失败。", grossDemandId);
            return ServiceResult<SourceDemandDeliveryItemDto>.Fail("DELIVERY_QUERY_FAILED", "读取来源需求交付上下文失败。");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, SourceDemandDeliveryItemDto>> VerifyForUpdateAsync(
        IReadOnlyCollection<SourceDeliveryClaim> claims,
        bool requireInventoryContext,
        CancellationToken cancellationToken = default)
    {
        RequireTransaction("来源交付额度复核");
        if (claims is null || claims.Count == 0
            || claims.Any(item => item.GrossDemandId == Guid.Empty || item.ExpectedDemandVersion <= 0 || item.Quantity <= 0))
            throw new SourceDeliveryException("INVALID_DELIVERY_CLAIM", "来源需求、版本和交付数量必须有效。");

        var grouped = claims.GroupBy(item => item.GrossDemandId)
            .ToDictionary(group => group.Key, group => new
            {
                Quantity = group.Sum(item => item.Quantity),
                Versions = group.Select(item => item.ExpectedDemandVersion).Distinct().ToArray(),
                SourceVersions = group.Select(item => item.ExpectedSourceVersion).Distinct().ToArray(),
                RequiresOrderPoolOwnership = group.Any(item => item.RequiresOrderPoolOwnership)
            });
        if (grouped.Values.Any(item => item.Versions.Length != 1 || item.SourceVersions.Length != 1))
            throw new SourceDeliveryException("SOURCE_VERSION_CONFLICT", "同一来源需求提交了不同的版本声明。");

        var contexts = (await repository.GetForUpdateAsync(grouped.Keys, cancellationToken))
            .ToDictionary(item => item.GrossDemandId);
        if (contexts.Count != grouped.Count)
            throw new SourceDeliveryException("SOURCE_DEMAND_NOT_FOUND", "部分来源需求不存在或历史身份不唯一。");

        foreach (var pair in grouped.OrderBy(item => item.Key))
        {
            var delivery = contexts[pair.Key];
            if (delivery.SourceType is not (DocumentTypeStaticsEnum.SalesOrder or DocumentTypeStaticsEnum.ComplaintReplenishment))
                throw new SourceDeliveryException("SOURCE_TYPE_NOT_DELIVERABLE", "来源类型不是认可的销售或投诉补货实物需求。");
            if (!delivery.IsActive)
                throw new SourceDeliveryException("SOURCE_INACTIVE", "来源需求已终止或失效，不能继续交付。");
            if (delivery.DemandVersion != pair.Value.Versions[0] || delivery.SourceVersion != pair.Value.SourceVersions[0])
                throw new SourceDeliveryException("SOURCE_CHANGED", "来源需求版本已变化，请重新选择或预览。");
            if (delivery.RemainingQty < pair.Value.Quantity)
                throw new SourceDeliveryException("DELIVERY_QUOTA_EXCEEDED", "来源需求可交付余额不足。");
            if (requireInventoryContext && !delivery.IsInventoryActionAllowed)
                throw new SourceDeliveryException(
                    string.IsNullOrWhiteSpace(delivery.InventoryBlockerCode) ? "INVENTORY_ACTION_BLOCKED" : delivery.InventoryBlockerCode,
                    "来源要求缺少产品、单位、客户或有效来源版本，不能执行正式库存动作。");
            if (pair.Value.RequiresOrderPoolOwnership && !delivery.OrderPoolSalesOrderId.HasValue)
                throw new SourceDeliveryException("ORDER_POOL_OWNERSHIP_REQUIRED", "该行实际消耗客供件，但缺少可证明且唯一的销售订单池归属。");
        }
        return contexts;
    }

    /// <inheritdoc />
    public async Task VerifyClosureAsync(
        string sourceType,
        Guid sourceUid,
        IReadOnlyDictionary<Guid, decimal> expectedLines,
        CancellationToken cancellationToken = default)
    {
        RequireTransaction("来源单据结案复核");
        ValidateSourceType(sourceType);
        if (sourceUid == Guid.Empty || expectedLines is null || expectedLines.Count == 0
            || expectedLines.Any(item => item.Key == Guid.Empty || item.Value <= 0))
            throw new SourceDeliveryException("INVALID_CLOSURE_CLAIM", "结案来源身份和实物要求必须有效。");

        var history = await repository.GetSourceForClosureForUpdateAsync(sourceType, sourceUid, cancellationToken);
        // 作废版本是已撤回历史，不得作为当前要求或完成证据。
        var current = history.Where(item => item.LifecycleStatus != GrossDemandLifecycleStatusEnum.Voided).ToList();
        if (current.Count != expectedLines.Count
            || current.GroupBy(item => item.SourceLineId).Any(group => group.Count() != 1)
            || current.Any(item => !expectedLines.ContainsKey(item.SourceLineId)))
            throw new SourceDeliveryException("SOURCE_MISMATCH", "来源记录缺失、重复或存在额外要求，不能结案。");

        foreach (var item in current)
        {
            if (item.DemandQty != expectedLines[item.SourceLineId]
                || item.DeliveredQty < 0 || item.DeliveredQty > item.DemandQty)
                throw new SourceDeliveryException("SOURCE_MISMATCH", "来源数量或正式交付事实异常，不能结案。");
            if (item.LifecycleStatus != GrossDemandLifecycleStatusEnum.Closed && item.DeliveredQty != item.DemandQty)
                throw new SourceDeliveryException("SOURCE_NOT_COMPLETED", "仍有未交付要求，请先完成正式交付或合法关闭要求。");
        }
    }

    private void RequireTransaction(string operation)
    {
        if (session.Transaction is null)
            throw new SourceDeliveryException("TRANSACTION_REQUIRED", $"{operation}必须在调用方事务内执行。");
    }

    private static void ValidateSourceType(string? sourceType)
    {
        if (string.IsNullOrWhiteSpace(sourceType)) return;
        var normalized = sourceType.Trim().ToUpperInvariant();
        if (normalized is not (DocumentTypeStaticsEnum.SalesOrder or DocumentTypeStaticsEnum.ComplaintReplenishment))
            throw new SourceDeliveryException("SOURCE_TYPE_NOT_DELIVERABLE", "只能处理销售或投诉补货来源需求。");
    }
}
