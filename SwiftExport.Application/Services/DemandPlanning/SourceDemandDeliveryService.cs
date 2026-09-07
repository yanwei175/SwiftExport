using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.DemandPlanning;

/// <summary>统一提供来源需求交付查询和事务内额度复核，避免 PL 直接以销售或 Allocation 作为第二选源入口。</summary>
public sealed class SourceDemandDeliveryService(
    ServiceBaseContext context,
    IGrossDemandRepository repository) : ISourceDemandDeliveryService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<SourceDemandDeliveryService> _logger = context.LoggerFactory.CreateLogger<SourceDemandDeliveryService>();

    /// <summary>查询销售与投诉补货的活动来源需求；缺少库存归属仍返回并附稳定阻断码。</summary>
    public async Task<ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>> SearchAsync(
        SourceDemandDeliverySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new SourceDemandDeliverySearchRequest();
            ValidateSourceType(request.SourceType);
            return ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>.Ok(
                await repository.SearchDeliverableAsync(request, cancellationToken));
        }
        catch (SourceDemandDeliveryException ex)
        {
            return ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>.Fail($"{ex.Code}：{ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询来源需求可交付列表失败。");
            return ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>.Fail("查询来源需求可交付列表失败。");
        }
    }

    /// <summary>读取单项来源需求上下文；不存在时返回明确失败。</summary>
    public async Task<ServiceResult<SourceDemandDeliveryItemDto>> GetAsync(
        Guid grossDemandId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (grossDemandId == Guid.Empty)
                throw new SourceDemandDeliveryException("INVALID_SOURCE_DEMAND", "来源需求标识不能为空。");
            var item = await repository.GetDeliveryContextAsync(grossDemandId, cancellationToken);
            return item is null
                ? ServiceResult<SourceDemandDeliveryItemDto>.Fail("SOURCE_DEMAND_NOT_FOUND：来源需求不存在。")
                : ServiceResult<SourceDemandDeliveryItemDto>.Ok(item);
        }
        catch (SourceDemandDeliveryException ex)
        {
            return ServiceResult<SourceDemandDeliveryItemDto>.Fail($"{ex.Code}：{ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取来源需求 {GrossDemandId} 交付上下文失败。", grossDemandId);
            return ServiceResult<SourceDemandDeliveryItemDto>.Fail("读取来源需求交付上下文失败。");
        }
    }

    /// <summary>在调用方事务内锁定来源需求并按需求行汇总声明量，防止并发 PL 超发或绕过来源终止。</summary>
    public async Task<IReadOnlyDictionary<Guid, SourceDemandDeliveryItemDto>> VerifyForUpdateAsync(
        IReadOnlyCollection<SourceDemandDeliveryClaim> claims,
        bool requireInventoryEligibility,
        CancellationToken cancellationToken = default)
    {
        if (_session.Transaction is null)
            throw new SourceDemandDeliveryException("TRANSACTION_REQUIRED", "来源交付额度复核必须在调用方事务内执行。");
        if (claims is null || claims.Count == 0 || claims.Any(item => item.GrossDemandId == Guid.Empty || item.ExpectedDemandVersion <= 0 || item.Quantity <= 0))
            throw new SourceDemandDeliveryException("INVALID_DELIVERY_CLAIM", "来源需求、版本和交付数量必须有效。");

        var grouped = claims.GroupBy(item => item.GrossDemandId)
            .ToDictionary(group => group.Key, group => new
            {
                Quantity = group.Sum(item => item.Quantity),
                Versions = group.Select(item => item.ExpectedDemandVersion).Distinct().ToArray(),
                SourceVersions = group.Select(item => item.ExpectedSourceVersion).Distinct().ToArray()
            });
        if (grouped.Values.Any(item => item.Versions.Length != 1 || item.SourceVersions.Length != 1))
            throw new SourceDemandDeliveryException("SOURCE_VERSION_CONFLICT", "同一来源需求提交了不同的版本声明。");

        var contexts = (await repository.GetDeliveryContextsForUpdateAsync(grouped.Keys, cancellationToken))
            .ToDictionary(item => item.GrossDemandId);
        if (contexts.Count != grouped.Count)
            throw new SourceDemandDeliveryException("SOURCE_DEMAND_NOT_FOUND", "部分来源需求不存在或历史身份不唯一。");

        foreach (var pair in grouped.OrderBy(item => item.Key))
        {
            var context = contexts[pair.Key];
            if (context.SourceType is not (DocumentTypeStaticsEnum.SalesOrder or DocumentTypeStaticsEnum.ComplaintReplenishment))
                throw new SourceDemandDeliveryException("SOURCE_TYPE_NOT_DELIVERABLE", "来源类型不是认可的销售或投诉补货实物需求。");
            if (!context.IsActive)
                throw new SourceDemandDeliveryException("SOURCE_INACTIVE", "来源需求已终止或失效，不能继续交付。");
            if (context.DemandVersion != pair.Value.Versions[0] || context.SourceVersion != pair.Value.SourceVersions[0])
                throw new SourceDemandDeliveryException("SOURCE_CHANGED", "来源需求版本已变化，请重新选择或预览。");
            if (context.RemainingQty < pair.Value.Quantity)
                throw new SourceDemandDeliveryException("DELIVERY_QUOTA_EXCEEDED", "来源需求可交付余额不足。");
            if (requireInventoryEligibility && !context.IsInventoryActionAllowed)
                throw new SourceDemandDeliveryException(
                    string.IsNullOrWhiteSpace(context.InventoryBlockerCode) ? "INVENTORY_ACTION_BLOCKED" : context.InventoryBlockerCode,
                    "来源需求缺少可证明的订单池、产品或单位，不能执行正式库存动作。");
        }
        return contexts;
    }

    private static void ValidateSourceType(string? sourceType)
    {
        if (string.IsNullOrWhiteSpace(sourceType)) return;
        var normalized = sourceType.Trim().ToUpperInvariant();
        if (normalized is not (DocumentTypeStaticsEnum.SalesOrder or DocumentTypeStaticsEnum.ComplaintReplenishment))
            throw new SourceDemandDeliveryException("SOURCE_TYPE_NOT_DELIVERABLE", "只能查询销售或投诉补货来源需求。");
    }
}
