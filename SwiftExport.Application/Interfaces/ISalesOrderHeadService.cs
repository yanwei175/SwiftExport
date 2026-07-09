using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SalesOrders.Requests;
using SwiftExport.Contracts.DTOs.SalesOrders.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
namespace SwiftExport.Application.Interfaces;

/// <summary>
/// 销售订单应用服务接口。
/// 统一承载：基础 CRUD（继承基类）+ 订单管理页 + 订单详情页。
/// </summary>
public partial interface ISalesOrderHeadService : IBaseService<SalesOrderHeader, SalesOrderHeaderDto>
{

    Task<ServiceResult<IReadOnlyList<SalesOrderHeaderDto>>> GetAllAsync();
    Task<ServiceResult<PagedResponseBase<SalesOrderHeaderDto>>> GetPagedListAsync(SalesOrderSearchRequest request);
    Task<ServiceResult<SalesOrderHeadBatchSaveResponse>> BatchSaveAsync(SalesOrderHeadBatchSaveRequest batchReq);




}
