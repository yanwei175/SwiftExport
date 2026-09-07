using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Orders;

/// <summary>
/// 销售订单 Manager 应用服务，负责列表查询和筛选建议，不承载详情写命令。
/// </summary>
public sealed class SalesOrderManagerService(
    ServiceBaseContext context,
    ISalesOrderHeadRepository headRepository,
    ICustomerRepository customerRepository) : ISalesOrderManagerService
{
    private readonly ILogger<SalesOrderManagerService> _logger =
        context.LoggerFactory.CreateLogger<SalesOrderManagerService>();

    /// <summary>
    /// 按管理页筛选条件分页查询销售订单，不加载完整订单详情。
    /// </summary>
    public Task<ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>> SearchAsync(
        SalesOrderManagerSearchRequest request,
        CancellationToken cancellationToken = default)
        => ExecuteReadAsync(
            () => headRepository.SearchManagerAsync(request, cancellationToken),
            "查询销售订单失败",
            cancellationToken);

    /// <summary>
    /// 获取客户代码建议列表，仅用于 Manager 页筛选输入提示。
    /// </summary>
    public Task<ServiceResult<List<string>>> GetCustomerCodesAsync(
        CancellationToken cancellationToken = default)
        => ExecuteReadAsync(
            () => customerRepository.GetSalesOrderCustomerCodesAsync(cancellationToken),
            "加载客户代码失败",
            cancellationToken);

    /// <summary>
    /// 执行只读查询并统一包装技术错误；调用方主动取消时继续传播取消异常。
    /// </summary>
    private async Task<ServiceResult<T>> ExecuteReadAsync<T>(
        Func<Task<T>> action,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ServiceResult<T>.Ok(await action());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{FailureMessage}", failureMessage);
            return ServiceResult<T>.Fail(failureMessage);
        }
    }
}
