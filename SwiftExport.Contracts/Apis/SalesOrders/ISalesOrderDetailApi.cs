using Refit;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.SalesOrderApis
{
    /// <summary>
    /// 销售订单 Detail 的读取、编辑及生命周期命令契约。
    /// </summary>
    public interface ISalesOrderDetailApi
    {
        /// <summary>获取指定销售订单的完整详情。</summary>
        [Get("/{uid}")]
        Task<ServiceResult<SalesOrderDetailDto>> GetAsync(
            Guid uid,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>查询销售订单明细可选择的产品。</summary>
        [Post("/products/search")]
        Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>> SearchProductsAsync(
            [Body] SalesOrderProductSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>查询销售订单可选择的客户。</summary>
        [Post("/customers/search")]
        Task<ServiceResult<PagedResponseBase<SalesOrderCustomerLookupDto>>> SearchCustomersAsync(
            [Body] SalesOrderCustomerSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>保存销售订单草稿。</summary>
        [Post("/save")]
        Task<ServiceResult<SaveSalesOrderResponse>> SaveAsync([Body] SaveSalesOrderRequest request);

        /// <summary>确认销售订单并接收来源需求。</summary>
        [Post("/{uid}/confirm")]
        Task<ServiceResult<SalesOrderConfirmationResultDto>> ConfirmAsync(
            Guid uid,
            [Body] ConfirmSalesOrderCommand request);

        /// <summary>在下游历史安全时取消销售订单确认。</summary>
        [Post("/{uid}/cancel-confirmation")]
        Task<ServiceResult<SalesOrderConfirmationResultDto>> CancelConfirmationAsync(
            Guid uid,
            [Body] CancelSalesOrderConfirmationCommand request);

        /// <summary>关闭销售订单。</summary>
        [Post("/{uid}/close")]
        Task<ServiceResult<SalesOrderDetailDto>> CloseAsync(
            Guid uid,
            [Body] CloseSalesOrderRequest request);

        /// <summary>删除未确认且无来源需求的销售订单草稿。</summary>
        [Post("/{uid}/delete-draft")]
        Task<ServiceResult<bool>> DeleteDraftAsync(
            Guid uid,
            [Body] SalesOrderCommandRequest request);
    }
}
