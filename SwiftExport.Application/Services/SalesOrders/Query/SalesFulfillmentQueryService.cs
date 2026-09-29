using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.SalesOrders.Query;
using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.SalesOrders.Query;

namespace SwiftExport.Application.Services.SalesOrders.Query;

/// <summary>只读编排销售订单的需求、采购、货好和正式交付证据。</summary>
public sealed class SalesFulfillmentQueryService(ServiceBaseContext context, ISalesOrderQueryRepository repository)
    : ISalesFulfillmentQueryService
{
    /// <summary>读取一次性投影；无效身份与不存在的订单返回业务失败，不启动写事务。</summary>
    public async Task<ServiceResult<SalesFulfillmentDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty) return ServiceResult<SalesFulfillmentDetailDto>.Fail("销售订单 Uid 不能为空。");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detail = await repository.GetFulfillmentAsync(uid, cancellationToken);
            return detail is null
                ? ServiceResult<SalesFulfillmentDetailDto>.Fail("未找到销售订单。")
                : ServiceResult<SalesFulfillmentDetailDto>.Ok(detail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            context.LoggerFactory.CreateLogger<SalesFulfillmentQueryService>()
                .LogError(ex, "读取销售履约证据失败：{SalesOrderId}", uid);
            return ServiceResult<SalesFulfillmentDetailDto>.Fail("加载销售履约证据失败。");
        }
    }
}
