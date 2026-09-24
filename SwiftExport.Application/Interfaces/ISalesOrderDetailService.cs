using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces;

/// <summary>
/// 定义销售订单 Detail 的读取、编辑及生命周期命令。
/// </summary>
public interface ISalesOrderDetailService
{
    /// <summary>读取指定销售订单的完整详情。</summary>
    Task<ServiceResult<SalesOrderDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default);

    /// <summary>查询销售订单行可选产品。</summary>
    Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>> SearchProductsAsync(SalesOrderProductSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>查询销售订单可选客户。</summary>
    Task<ServiceResult<PagedResponseBase<SalesOrderCustomerLookupDto>>> SearchCustomersAsync(SalesOrderCustomerSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>保存销售订单草稿。</summary>
    Task<ServiceResult<SaveSalesOrderResponse>> SaveAsync(SaveSalesOrderRequest request);

    /// <summary>在下游历史安全时取消销售订单确认。</summary>
    Task<ServiceResult<SalesOrderConfirmationResultDto>> CancelConfirmationAsync(Guid uid, CancelSalesOrderConfirmationCommand request);

    /// <summary>关闭销售订单。</summary>
    Task<ServiceResult<SalesOrderDetailDto>> CloseAsync(Guid uid, CloseSalesOrderRequest request);

    /// <summary>删除未确认且无来源需求的销售订单草稿。</summary>
    Task<ServiceResult<bool>> DeleteDraftAsync(Guid uid, SalesOrderCommandRequest request);
}
