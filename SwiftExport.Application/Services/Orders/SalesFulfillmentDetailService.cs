using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Orders;

namespace SwiftExport.Application.Services.Orders;

/// <summary>读取单张销售订单的只读履约 Detail，并保持不存在与查询失败语义清晰。</summary>
public sealed class SalesFulfillmentDetailService(
    ServiceBaseContext context,
    ISalesFulfillmentDetailRepository repository) : ISalesFulfillmentDetailService
{
    private readonly ILogger<SalesFulfillmentDetailService> _logger = context.LoggerFactory.CreateLogger<SalesFulfillmentDetailService>();

    /// <inheritdoc/>
    public async Task<ServiceResult<SalesFulfillmentDetailDto>> GetAsync(
        Guid salesOrderId,
        CancellationToken cancellationToken = default)
    {
        if (salesOrderId == Guid.Empty)
            return ServiceResult<SalesFulfillmentDetailDto>.Fail("INVALID_SALES_ORDER", "销售订单标识无效。");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detail = await repository.GetAsync(salesOrderId, cancellationToken);
            return detail is null
                ? ServiceResult<SalesFulfillmentDetailDto>.Fail("SALES_ORDER_NOT_FOUND", "未找到销售订单。")
                : ServiceResult<SalesFulfillmentDetailDto>.Ok(detail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询销售订单 {SalesOrderId} 履约 Detail 失败。", salesOrderId);
            return ServiceResult<SalesFulfillmentDetailDto>.Fail("查询销售履约详情失败。");
        }
    }
}
