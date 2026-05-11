using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Orders
{
    /// <summary>
    /// 采购订单 API。
    /// </summary>
    public interface IPurchaseOrderApi : IGenericApi<PurchaseOrderHeadDto>
    {
        /// <summary>
        /// 分页查询采购订单头。
        /// </summary>
        [Post("/search")]
        Task<ServiceResult<PagedResultDto<PurchaseOrderHeadDto>>> GetPagedListAsync([Body] PurchaseOrderSearchDto search);

        /// <summary>
        /// 根据头表ID获取采购订单详情。
        /// </summary>
        [Get("/{id}/detail")]
        Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByIdAsync(int id);

        /// <summary>
        /// 根据采购单号（OrderNo）获取采购订单详情。
        /// </summary>
        [Get("/by-orderno/{orderNo}")]
        Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByOrderNoAsync(string orderNo);

        /// <summary>
        /// 根据自定义编号（PoNo）获取采购订单详情。
        /// </summary>
        [Get("/by-pono/{poNo}")]
        Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByPoNoAsync(string poNo);

        /// <summary>
        /// 保存采购订单详情。
        /// </summary>
        [Post("/detail/save")]
        Task<ServiceResult<int>> SaveDetailAsync([Body] PurchaseOrderDetailDto request);
    }
}
