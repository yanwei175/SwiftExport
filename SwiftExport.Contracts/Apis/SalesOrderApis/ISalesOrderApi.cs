using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.SalesOrderApis
{
    /// <summary>
    /// 销售订单 API 接口定义（Refit）。
    /// 支持：
    /// 1. 订单管理页（主从表）
    /// 2. 订单详情页（单据编辑）
    /// </summary>
    public interface ISalesOrderApi : IGenericApi<SalesOrderHeaderDto>
    {

        #region 订单管理页（主从表）

        /// <summary>
        /// 中文注释：分页查询订单头列表（用于 SalesOrderManagerViewModel 主表）。
        /// 支持多选状态、订单号/发票号、客户代码、颜色、包含 SKU 等筛选条件。
        /// </summary>
        [Post("/headers/search-manager")]
        Task<ServiceResult<PagedResultDto<SalesOrderHeaderManagerDto>>> GetSalesOrderHeaderManagerPagedListAsync([Body] SalesOrderManagerSearchDto search);
        /// <summary>
        /// 中文注释：根据订单 ID 获取订单头信息（用于 SalesOrderManagerViewModel 从表动态加载）。
        /// </summary>
        [Get("/{orderId}/header-manager")]
        Task<ServiceResult<SalesOrderHeaderManagerDto>> GetSalesOrderHeaderManagerByIdAsync(Guid orderId);


        /// <summary>
        /// 中文注释：根据订单 ID 获取订单行列表（用于 SalesOrderManagerViewModel 从表动态加载）。
        /// </summary>
        [Get("/{orderId}/line-manager")]
        Task<ServiceResult<List<SalesOrderLineManagerDto>>> GetSalesOrderLineManagerAsync(Guid orderId);

        /// <summary>
        /// 删除单个销售订单,目前是关闭订单,将订单的IsDelete设置为1.
        /// </summary>
        [Delete("/{orderId}")]
        Task<ServiceResult<bool>> DeleteSalesOrderAsync(Guid orderId, [Query] int version);

        /// <summary>
        /// 管理页修改表头
        /// </summary>
        [Post("/header-manager-update")]
        Task<ServiceResult<BatchSalesOrderHeadManagerUpdateResultDto>> BatchUpdateHeadManagerAsync([Body] BatchSalesOrderHeadManagerUpdateRequestDto request);


        /// <summary>
        /// 确认销售订单。
        /// </summary>
        [Post("/confirm")]
        Task<ServiceResult<ConfirmationItemResponse>> ConfirmSalesOrdersAsync([Body] ConfirmationItemRequest request);
        /// <summary>
        /// 批量确认销售订单。
        /// </summary>
        [Post("/batch-confirm")]
        Task<ServiceResult<BatchConfirmationResponse>> BatchConfirmSalesOrdersAsync([Body] BatchConfirmationRequest request);

        /// <summary>
        /// 取消确认销售订单。
        /// </summary>
        [Post("/cancel-confirm")]
        Task<ServiceResult<ConfirmationItemResponse>> CancelConfirmSalesOrdersAsync([Body] ConfirmationItemRequest request);

        #endregion

        #region 订单详情页（单据编辑）
        /// <summary>/ 获取销售订单详情（订单头 + 明细行）</summary>
        [Get("/{orderId}/detail")]
        Task<ServiceResult<SalesOrderDetailDto>> GetSalesOrderDetailAsync(Guid orderId);

        /// <summary>订单详情页一键保存接口（新增/编辑由后端根据 Header.Id 判断）。</summary>
        [Post("/detail-save")]
        Task<ServiceResult<SalesOrderSaveDetailResponse>> SaveSalesOrderDetailAsync([Body] SalesOrderSaveDetailRequest request);

        /// <summary>中文注释：根据订单 ID 获取订单行列表。 </summary>
        [Get("/{orderId}/lines")]
        Task<ServiceResult<List<SalesOrderLineDto>>> GetSalesOrderLinesAsync(Guid orderId);


        /// <summary>
        /// 中文注释：销售订单行数据源分页查询。
        /// 从 CustomerProduct 与 Product 联查结果中返回可用于“新增订单行”的基础数据。
        /// </summary>
        [Post("/lines/sales-products/search")]
        Task<ServiceResult<PagedResultDto<SalesProductDto>>> GetSalesProductPagedListAsync([Body] SalesProductSearchDto request);
        /// <summary>
        /// 获取销售订单下一单号。
        /// </summary>
        [Post("/next-order-no")]
        Task<ServiceResult<DocumentNoResultDto>> GetNextOrderNoAsync();

        /// <summary>
        /// 分页查询销售订单列表（旧 DTO，保留兼容）
        /// </summary>
        [Post("/search")]
        Task<ServiceResult<PagedResultDto<SalesOrderDto>>> GetSalesOrderPagedListAsync([Body] SalesOrderSearchDto search);











        #endregion
    }
}
