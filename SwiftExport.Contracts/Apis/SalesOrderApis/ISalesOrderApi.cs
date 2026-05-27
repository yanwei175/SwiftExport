using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
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
        #region 兼容旧接口（已有）

        /// <summary>
        /// 分页查询销售订单列表（旧 DTO，保留兼容）
        /// </summary>
        [Post("/search")]
        Task<ServiceResult<PagedResultDto<SalesOrderDto>>> GetSalesOrderPagedListAsync([Body] SalesOrderSearchDto search);

        /// <summary>
        /// 获取销售订单详情（订单头 + 明细行）
        /// </summary>
        [Get("/{orderId}/detail")]
        Task<ServiceResult<SalesOrderDetailDto>> GetSalesOrderDetailAsync(int orderId);

        /// <summary>
        /// 创建销售订单
        /// </summary>
        [Post("/create")]
        Task<ServiceResult<int>> CreateSalesOrderAsync([Body] SalesOrderDetailDto request);

        /// <summary>
        /// 更新销售订单
        /// </summary>
        [Put("/{orderId}/update")]
        Task<ServiceResult<bool>> UpdateSalesOrderAsync(int orderId, [Body] SalesOrderDetailDto request);

        /// <summary>
        /// 删除单个销售订单
        /// </summary>
        [Delete("/{orderId}")]
        Task<ServiceResult<bool>> DeleteSalesOrderAsync(int orderId);

        /// <summary>
        /// 批量删除销售订单
        /// </summary>
        [Post("/batch-delete")]
        Task<ServiceResult<bool>> BatchDeleteSalesOrderAsync([Body] List<int> orderIds);

        #endregion

        #region 订单管理页（主从表）

        /// <summary>
        /// 中文注释：分页查询订单头列表（用于 SalesOrderManagerViewModel 主表）。
        /// 支持多选状态、订单号/发票号、客户代码、颜色、包含 SKU 等筛选条件。
        /// </summary>
        [Post("/headers/search")]
        Task<ServiceResult<PagedResultDto<SalesOrderHeaderDto>>> GetSalesOrderHeaderPagedListAsync([Body] SalesOrderManagerSearchDto search);

        /// <summary>
        /// 中文注释：根据订单 ID 获取订单行列表（用于 SalesOrderManagerViewModel 从表动态加载）。
        /// </summary>
        [Get("/{orderId}/lines")]
        Task<ServiceResult<List<SalesOrderLineDto>>> GetSalesOrderLinesAsync(int orderId);

        /// <summary>
        /// 中文注释：订单管理页保存接口。
        /// 一次提交：
        /// 1. 主表头批量变更（新增/更新/删除）
        /// 2. 当前选中订单的行变更（新增/更新/删除）
        /// </summary>
        [Post("/manager/save")]
        Task<ServiceResult<bool>> SaveSalesOrderManagerAsync([Body] SalesOrderManagerSaveDto request);

        #endregion

        #region 订单详情页（单据编辑）

        /// <summary>
        /// 中文注释：订单详情页一键保存接口（新增/编辑由后端根据 Header.Id 判断）。
        /// </summary>
        [Post("/detail/save")]
        Task<ServiceResult<int>> SaveSalesOrderDetailAsync([Body] SalesOrderDetailDto request);


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
        Task<ServiceResult<DocumentNoResultDto>> GetNextOrderNoAsync([Body] DocumentNoRequestDto request);

        /// <summary>
        /// 批准一张销售订单。
        /// </summary>
        [Post("/{orderId}/approve")]
        Task<ServiceResult<bool>> ApproveSalesOrderAsync(int orderId);

        /// <summary>
        /// 驳回一张销售订单。
        /// </summary>
        [Post("/{orderId}/reject")]
        Task<ServiceResult<bool>> RejectSalesOrderAsync(int orderId);

        /// <summary>
        /// 批量批准销售订单。
        /// </summary>
        [Post("/batch-approve")]
        Task<ServiceResult<List<string>>> BatchApproveSalesOrdersAsync([Body] List<int> orderIds);

        /// <summary>
        /// 批量驳回销售订单。
        /// </summary>
        [Post("/batch-reject")]
        Task<ServiceResult<List<string>>> BatchRejectSalesOrdersAsync([Body] List<int> orderIds);

        #endregion
    }
}
