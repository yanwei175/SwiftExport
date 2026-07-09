using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SalesOrders.Requests;
using SwiftExport.Contracts.DTOs.SalesOrders.Responses;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Application.Interfaces;

public interface ISalesOrderDetailService
{
    /// <summary>
    /// 获取订单详情（头+行）。
    /// </summary>
    Task<ServiceResult<SalesOrderDetailDto>> GetSalesOrderDetailByHeadIdAsync(Guid orderUid);
    /// <summary> 查询下一销售订单号（单据编号生成规则：SO+年月+4位流水号）。</summary>
    Task<ServiceResult<DocumentNoResponse>> GetNextSalesOrderNoAsync();
    /// <summary> 获取销售订单行的产品分页建议列表（根据客户、颜色、客户编码、批量客户SKU列表等条件）。</summary>
    Task<ServiceResult<PagedResponseBase<SalesProductDto>>> GetSalesProductPagedListAsync(SalesProductSearchRequest request);
    /// <summary>
    /// 保存一张订单。
    /// </summary>
    Task<ServiceResult<SalesOrderSaveDetailResponse>> SaveSalesOrderAsync(SalesOrderSaveDetailRequest request);
}
