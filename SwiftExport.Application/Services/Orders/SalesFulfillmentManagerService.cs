using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Orders;

namespace SwiftExport.Application.Services.Orders;

/// <summary>校验销售履约 Manager 查询边界并包装只读仓储结果。</summary>
public sealed class SalesFulfillmentManagerService(
    ServiceBaseContext context,
    ISalesFulfillmentManagerRepository repository) : ISalesFulfillmentManagerService
{
    private readonly ILogger<SalesFulfillmentManagerService> _logger = context.LoggerFactory.CreateLogger<SalesFulfillmentManagerService>();

    /// <inheritdoc/>
    public async Task<ServiceResult<PagedResponseBase<SalesFulfillmentManagerListItemDto>>> SearchAsync(
        SalesFulfillmentManagerSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Validate(request);
            cancellationToken.ThrowIfCancellationRequested();
            return ServiceResult<PagedResponseBase<SalesFulfillmentManagerListItemDto>>.Ok(
                await repository.SearchAsync(request, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询销售履约 Manager 失败。");
            return ServiceResult<PagedResponseBase<SalesFulfillmentManagerListItemDto>>.Fail("查询销售履约失败。");
        }
    }

    /// <summary>限制分页、筛选文本和共享枚举，避免无界或无效条件进入 SQL。</summary>
    private static void Validate(SalesFulfillmentManagerSearchRequest request)
    {
        if (request is null
            || request.PageIndex < 1
            || request.PageSize is < 1 or > 200
            || (long)(request.PageIndex - 1) * request.PageSize > int.MaxValue
            || request.Keyword?.Length > 100
            || request.OrderNo?.Length > 100
            || request.CustomerCode?.Length > 100
            || request.FulfillmentStatuses is null
            || request.FulfillmentStatuses.Count > 20
            || request.FulfillmentStatuses.Any(status => !Enum.IsDefined(status)))
            throw new InvalidOperationException("销售履约筛选参数无效。");
    }
}
