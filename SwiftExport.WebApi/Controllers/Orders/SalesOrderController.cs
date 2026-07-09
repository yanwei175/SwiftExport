using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SalesOrders.Requests;
using SwiftExport.Contracts.DTOs.SalesOrders.Responses;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.Orders;

/// <summary>
/// 销售控制器。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SalesOrderController(ISalesOrderManagerService managerService,
    ISalesOrderDetailService detailService, ISalesOrderLineService lineService) : ControllerBase
{

    #region 订单管理页（主从表）

    /// <summary>
    /// 中文注释：分页查询订单头列表（用于 SalesOrderManagerViewModel 主表）。
    /// 支持多选状态、订单号/发票号、客户代码、颜色、包含 SKU 等筛选条件。
    /// </summary>
    [HttpPost("headers/search-manager")]
    public async Task<ServiceResult<PagedResponseBase<SalesOrderHeaderManagerDto>>> GetSalesOrderHeaderManagerPagedListAsync([FromBody] SalesOrderManagerSearchRequest search)
    {
        return await managerService.GetSalesOrderHeaderManagerPagedListAsync(search);
    }

    /// <summary>根据订单 ID 获取订单头信息（用于 SalesOrderManagerViewModel 从表动态加载）。</summary>
    [HttpGet("{orderId}/header-manager")]
    public async Task<ServiceResult<SalesOrderHeaderManagerDto>> GetSalesOrderHeaderManagerByIdAsync(Guid orderId)
    {
        return await managerService.GetSalesOrderHeaderManagerByIdAsync(orderId);
    }

    /// <summary>根据订单 ID 获取订单行列表（用于 SalesOrderManagerViewModel 从表动态加载）。</summary>
    [HttpGet("{orderId}/line-manager")]
    public async Task<ServiceResult<List<SalesOrderLineManagerDto>>> GetSalesOrderLineManagerAsync(Guid orderId)
    {
        return await managerService.GetSalesOrderLineManagerAsync(orderId);
    }

    /// <summary>
    /// 删除单个销售订单
    /// </summary>
    [HttpDelete("{orderId}")]
    public async Task<ServiceResult<bool>> DeleteSalesOrderAsync([FromRoute] Guid orderId, [FromQuery] int version)
    {
        return await managerService.DeleteSalesOrderAsync(orderId, version);
    }

    /// <summary>
    /// 管理页修改表头
    /// </summary>
    [HttpPost("header-manager-update")]
    public async Task<ServiceResult<BatchSalesOrderHeadManagerUpdateResultDto>> BatchUpdateHeadManagerAsync([FromBody] BatchSalesOrderHeadManagerUpdateRequestDto request)
    {
        return await managerService.BatchUpdateHeadManagerAsync(request);
    }



    /// <summary>批准一张销售订单。</summary>
    [HttpPost("confirm")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<ConfirmationItemResponse>> ConfirmSalesOrderAsync([FromBody] ConfirmationItemRequest request)
    {
        return await managerService.ConfirmSalesOrderAsync(request);
    }

    /// <summary>批量批准销售订单。</summary>
    [HttpPost("batch-confirm")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<BatchConfirmationResponse>> BatchConfirmSalesOrdersAsync([FromBody] BatchConfirmationRequest request)
    {
        return await managerService.BatchConfirmSalesOrdersAsync(request);
    }

    /// <summary>驳回一张销售订单。</summary>
    [HttpPost("cancel-confirm")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<ConfirmationItemResponse>> CancelConfirmSalesOrderAsync([FromBody] ConfirmationItemRequest request)
    {
        return await managerService.CancelConfirmSalesOrderAsync(request);
    }


    #endregion

    #region 订单详情页（单据编辑）

    /// <summary>
    /// 中文注释：获取销售订单详情（订单头 + 明细行）。
    /// </summary>
    [HttpGet("{orderId}/detail")]
    public async Task<ServiceResult<SalesOrderDetailDto>> GetSalesOrderDetailAsync(Guid orderId)
    {
        return await detailService.GetSalesOrderDetailByHeadIdAsync(orderId);
    }

    /// <summary>
    /// 中文注释：订单详情页一键保存接口（新增/编辑由后端根据 Header.Uid 判断）。
    /// </summary>
    [HttpPost("detail-save")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<SalesOrderSaveDetailResponse>> SaveSalesOrderDetailAsync([FromBody] SalesOrderSaveDetailRequest request)
    {
        return await detailService.SaveSalesOrderAsync(request);
    }

    /// <summary>中文注释：根据订单 ID 获取订单行列表。 </summary>
    [HttpGet("{orderId}/lines")]
    public async Task<ServiceResult<List<SalesOrderLineDto>>> GetSalesOrderLinesAsync(Guid orderId)
    {
        return await lineService.GetLineListByHeadUidAsync(orderId);
    }

    [HttpPost("lines/sales-products/search")]
    public async Task<ServiceResult<PagedResponseBase<SalesProductDto>>> GetSalesProductPagedListAsync([FromBody] SalesProductSearchRequest request)
    {
        return await detailService.GetSalesProductPagedListAsync(request);
    }

    /// <summary>中文注释：获取销售订单下一单号(SO)</summary>
    [HttpPost("next-order-no")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<DocumentNoResponse>> GetNextOrderNoAsync()
    {
        return await detailService.GetNextSalesOrderNoAsync();
    }




    #endregion
}

