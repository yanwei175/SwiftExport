using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.Orders;

/// <summary>
/// 销售订单控制器。
/// 覆盖 ISalesOrderApi 中定义的所有路由与行为。
/// </summary>
public class SalesOrderController(ISalesOrderHeadService service,ISalesOrderLineSourceService lineService)
    : BaseController<SalesOrderHeader, SalesOrderHeaderDto>(service)
{
    #region 兼容旧接口（已有）

    /// <summary>
    /// 中文注释：获取销售订单详情（订单头 + 明细行）。
    /// </summary>
    [HttpGet("{orderId}/detail")]
    public async Task<ServiceResult<SalesOrderDetailDto>> GetSalesOrderDetailAsync(int orderId)
    {
        return await service.GetSalesOrderDetailAsync(orderId);
    }

    /// <summary>
    /// 中文注释：创建销售订单。
    /// </summary>
    [HttpPost("create")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> CreateSalesOrderAsync([FromBody] SalesOrderDetailDto request)
    {
        return await service.CreateSalesOrderAsync(request);
    }

    /// <summary>
    /// 中文注释：更新销售订单。
    /// </summary>
    [HttpPut("{orderId}/update")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<bool>> UpdateSalesOrderAsync(int orderId, [FromBody] SalesOrderDetailDto request)
    {
        return await service.UpdateSalesOrderAsync(orderId, request);
    }

    /// <summary>
    /// 中文注释：删除单个销售订单。
    /// </summary>
    [HttpDelete("{orderId}")]
    public async Task<ServiceResult<bool>> DeleteSalesOrderAsync(int orderId)
    {
        return await service.DeleteSalesOrderAsync(orderId);
    }

    /// <summary>
    /// 中文注释：批量删除销售订单。
    /// </summary>
    [HttpPost("batch-delete")]
    public async Task<ServiceResult<bool>> BatchDeleteSalesOrderAsync([FromBody] List<int> orderIds)
    {
        return await service.BatchDeleteSalesOrderAsync(orderIds);
    }

    #endregion

    #region 订单管理页（主从表）

    /// <summary>
    /// 中文注释：分页查询订单头列表（用于 SalesOrderManagerViewModel 主表）。
    /// 支持多选状态、订单号/发票号、客户代码、颜色、包含 SKU 等筛选条件。
    /// </summary>
    [HttpPost("headers/search")]
    public async Task<ServiceResult<PagedResultDto<SalesOrderHeaderDto>>> GetSalesOrderHeaderPagedListAsync([FromBody] SalesOrderManagerSearchDto search)
    {
        return await service.GetSalesOrderHeaderPagedListAsync(search);
    }

    /// <summary>
    /// 中文注释：根据订单 ID 获取订单行列表（用于 SalesOrderManagerViewModel 从表动态加载）。
    /// </summary>
    [HttpGet("{orderId}/lines")]
    public async Task<ServiceResult<List<SalesOrderLineDto>>> GetSalesOrderLinesAsync(int orderId)
    {
        return await service.GetSalesOrderLinesAsync(orderId);
    }

    /// <summary>
    /// 中文注释：订单管理页保存接口。
    /// 一次提交：
    /// 1. 主表头批量变更（新增/更新/删除）
    /// 2. 当前选中订单的行变更（新增/更新/删除）
    /// </summary>
    [HttpPost("manager/save")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<bool>> SaveSalesOrderManagerAsync([FromBody] SalesOrderManagerSaveDto request)
    {
        return await service.SaveSalesOrderManagerAsync(request);
    }


    #endregion

    #region 订单详情页（单据编辑）

    /// <summary>
    /// 中文注释：订单详情页一键保存接口（新增/编辑由后端根据 Header.Id 判断）。
    /// </summary>
    [HttpPost("detail/save")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> SaveSalesOrderDetailAsync([FromBody] SalesOrderDetailDto request)
    {
        return await service.SaveSalesOrderDetailAsync(request);
    }


    [HttpPost("lines/sales-products/search")]
    public async Task<ServiceResult<PagedResultDto<SalesProductDto>>> GetSalesProductPagedListAsync([FromBody] SalesProductSearchDto request)
    {
        return await lineService.GetSalesProductPagedListAsync(request);
    }

    /// <summary>
    /// 中文注释：获取销售订单下一单号（SO）。
    /// </summary>
    [HttpPost("next-order-no")]
    public async Task<ServiceResult<DocumentNoResultDto>> GetNextOrderNoAsync([FromBody] DocumentNoRequestDto request)
    {
        return await service.GetNextOrderNoAsync(request);
    }

    /// <summary>
    /// 批准一张销售订单。
    /// </summary>
    [HttpPost("{orderId}/approve")]
    [PreventDuplicateRequest]   
    public async Task<ServiceResult<bool>> ApproveSalesOrderAsync(int orderId)
    {
        return await service.ApproveSalesOrderAsync(orderId);
    }

    /// <summary>
    /// 驳回一张销售订单。
    /// </summary>
    [HttpPost("{orderId}/reject")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<bool>> RejectSalesOrderAsync(int orderId)
    {
        return await service.RejectSalesOrderAsync(orderId);
    }

    /// <summary>
    /// 批量批准销售订单。
    /// </summary>
    [HttpPost("batch-approve")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<List<int>>> BatchApproveSalesOrdersAsync([FromBody] List<int> orderIds)
    {
        return await service.BatchApproveSalesOrdersAsync(orderIds);
    }

    /// <summary>
    /// 批量驳回销售订单。
    /// </summary>
    [HttpPost("batch-reject")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<List<string>>> BatchRejectSalesOrdersAsync([FromBody] List<int> orderIds)
    {
        return await service.BatchRejectSalesOrdersAsync(orderIds);
    }

    #endregion
}

