using SwiftExport.Core.Interfaces.Customers.Management;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.SalesOrders.Document;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Models;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Exceptions;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Data;
using System.Text.Json;

using SwiftExport.Application.Services.SalesOrders.Internal;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Application.Interfaces.SalesOrders.Query;
using SwiftExport.Core.Interfaces.SalesOrders.Query;
namespace SwiftExport.Application.Services.SalesOrders.Query;

/// <summary>读取销售订单列表、详情和客户产品候选，不拥有写事务。</summary>
public sealed class SalesOrderQueryService(ServiceBaseContext context, ISalesOrderQueryRepository queryRepository, ICustomerRepository customerRepository, IProductRepository productRepository) : ISalesOrderQueryService
{
    /// <summary>
    /// 按管理页筛选条件分页查询销售订单，不加载完整订单详情。
    /// </summary>
    public Task<ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>> SearchAsync(
        SalesOrderManagerSearchRequest request,
        CancellationToken cancellationToken = default)
        => ExecuteReadAsync(
            () => queryRepository.SearchManagerAsync(request, DateTime.UtcNow.AddHours(8).Date, cancellationToken),
            "查询销售订单失败",
            cancellationToken,
            detailValidation: false);

    /// <summary>
    /// 获取客户代码建议列表，仅用于 Manager 页筛选输入提示。
    /// </summary>
    public Task<ServiceResult<List<string>>> GetCustomerCodesAsync(
        CancellationToken cancellationToken = default)
        => ExecuteReadAsync(
            () => customerRepository.GetSalesOrderCustomerCodesAsync(cancellationToken),
            "加载客户代码失败",
            cancellationToken,
            detailValidation: false);

    /// <summary>
    /// 加载一个销售订单的完整编辑文档，供详情 Tab 初始化或保存后刷新。
    /// </summary>
    public async Task<ServiceResult<SalesOrderDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty) return ServiceResult<SalesOrderDetailDto>.Fail("销售订单 Uid 不能为空。");
        return await ExecuteReadAsync(async () =>
        {
            var document = await queryRepository.GetDetailAsync(uid, cancellationToken);
            return document ?? throw new SalesOrderValidationException("未找到销售订单。");
        }, "加载销售订单失败", cancellationToken);
    }

    /// <summary>
    /// 查询销售订单行可选择的产品，同时返回客户产品关系和价格快照所需信息。
    /// </summary>
    public Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>> SearchProductsAsync(
        SalesOrderProductSearchRequest request,
        CancellationToken cancellationToken = default)
        => ExecuteReadAsync(
            () => productRepository.SearchSalesOrderProductsAsync(request, cancellationToken),
            "查询销售产品失败",
            cancellationToken);

    /// <summary>
    /// 查询销售订单头可选择的客户，用于客户 Lookup。
    /// </summary>
    public Task<ServiceResult<PagedResponseBase<SalesOrderCustomerLookupDto>>> SearchCustomersAsync(
        SalesOrderCustomerSearchRequest request,
        CancellationToken cancellationToken = default)
        => ExecuteReadAsync(
            () => customerRepository.SearchSalesOrderCustomersAsync(request, cancellationToken),
            "查询销售客户失败",
            cancellationToken);

    /// <summary>只读入口保留原异常映射和取消行为，不开启写命令事务。</summary>
    private async Task<ServiceResult<T>> ExecuteReadAsync<T>(Func<Task<T>> action,
        string failureMessage, CancellationToken cancellationToken, bool detailValidation = true)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ServiceResult<T>.Ok(await action());
        }
        catch (SalesOrderValidationException ex) when (detailValidation)
        { return ServiceResult<T>.Fail(ex.Message); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { throw; }
        catch (Exception ex)
        {
            context.LoggerFactory.CreateLogger<SalesOrderQueryService>().LogError(ex, "{FailureMessage}", failureMessage);
            return ServiceResult<T>.Fail(failureMessage);
        }
    }

}
