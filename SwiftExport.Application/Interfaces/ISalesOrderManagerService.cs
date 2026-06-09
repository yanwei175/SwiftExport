using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Application.Interfaces
{
    public interface ISalesOrderManagerService
    {
        /// <summary>订单管理页：分页查询订单头（用于 SalesOrderManagerViewModel 主表）。</summary>
        Task<ServiceResult<PagedResultDto<SalesOrderHeaderManagerDto>>> GetSalesOrderHeaderManagerPagedListAsync(SalesOrderManagerSearchDto search);
        Task<ServiceResult<SalesOrderHeaderManagerDto>> GetSalesOrderHeaderManagerByIdAsync(Guid orderId);
        Task<ServiceResult<List<SalesOrderLineManagerDto>>> GetSalesOrderLineManagerAsync(Guid orderId);



        /// <summary>
        /// 批准一张销售订单（单据状态流转：草稿->已批准）。
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        Task<ServiceResult<ConfirmationItemResponse>> ConfirmSalesOrderAsync(ConfirmationItemRequest request);
        /// <summary>
        /// 驳回一张销售订单
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        Task<ServiceResult<ConfirmationItemResponse>> CancelConfirmSalesOrderAsync(ConfirmationItemRequest request);
        /// <summary>
        /// 批量批准销售订单
        /// </summary>
        /// <param name="request">批量操作请求</param>
        /// <returns>操作结果</returns>
        Task<ServiceResult<BatchConfirmationResponse>> BatchConfirmSalesOrdersAsync(BatchConfirmationRequest request);

    }
}
