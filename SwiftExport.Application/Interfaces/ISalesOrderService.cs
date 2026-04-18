using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Application.Interfaces;

/// <summary>
/// 销售订单应用服务接口。
/// 统一承载：基础 CRUD（继承基类）+ 订单管理页 + 订单详情页。
/// </summary>
public interface ISalesOrderService : IBaseService<SalesOrderHeader, SalesOrderDto>
{
    /// <summary>
    /// 兼容旧接口：分页查询销售订单列表。
    /// </summary>
    Task<ServiceResult<PagedResultDto<SalesOrderDto>>> GetSalesOrderPagedListAsync(SalesOrderSearchDto search);

    /// <summary>
    /// 兼容旧接口：获取订单详情（头+行）。
    /// </summary>
    Task<ServiceResult<SalesOrderDetailDto>> GetSalesOrderDetailAsync(int orderId);

    /// <summary>
    /// 兼容旧接口：创建订单。
    /// </summary>
    Task<ServiceResult<int>> CreateSalesOrderAsync(SalesOrderDetailDto request);

    /// <summary>
    /// 兼容旧接口：更新订单。
    /// </summary>
    Task<ServiceResult<bool>> UpdateSalesOrderAsync(int orderId, SalesOrderDetailDto request);

    /// <summary>
    /// 兼容旧接口：删除单个订单。
    /// </summary>
    Task<ServiceResult<bool>> DeleteSalesOrderAsync(int orderId);

    /// <summary>
    /// 兼容旧接口：批量删除订单。
    /// </summary>
    Task<ServiceResult<bool>> BatchDeleteSalesOrderAsync(List<int> orderIds);

    /// <summary>
    /// 订单管理页：分页查询订单头。
    /// </summary>
    Task<ServiceResult<PagedResultDto<SalesOrderHeaderDto>>> GetSalesOrderHeaderPagedListAsync(SalesOrderManagerSearchDto search);

    /// <summary>
    /// 订单管理页：按订单ID获取明细行。
    /// </summary>
    Task<ServiceResult<List<SalesOrderLineDto>>> GetSalesOrderLinesAsync(int orderId);

    /// <summary>
    /// 订单管理页：主从批量保存。
    /// </summary>
    Task<ServiceResult<bool>> SaveSalesOrderManagerAsync(SalesOrderManagerSaveDto request);

    /// <summary>
    /// 订单管理页：订单状态筛选字典。
    /// </summary>
    Task<ServiceResult<List<SalesOrderStatusOptionDto>>> GetOrderStatusOptionsAsync();

    /// <summary>
    /// 订单管理页：审批状态筛选字典。
    /// </summary>
    Task<ServiceResult<List<SalesOrderStatusOptionDto>>> GetApprovalStatusOptionsAsync();

    /// <summary>
    /// 订单详情页：新增/编辑统一保存。
    /// </summary>
    Task<ServiceResult<int>> SaveSalesOrderDetailAsync(SalesOrderDetailDto request);
}
