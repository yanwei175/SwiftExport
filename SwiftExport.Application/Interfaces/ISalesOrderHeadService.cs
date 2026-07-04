using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using System;
using SwiftExport.Core.Common;

namespace SwiftExport.Application.Interfaces;

/// <summary>
/// 销售订单应用服务接口。
/// 统一承载：基础 CRUD（继承基类）+ 订单管理页 + 订单详情页。
/// </summary>
public partial interface ISalesOrderHeadService
{
    Task<ServiceResult<SalesOrderHeaderDto>> GetByIdAsync(Guid uid);
    Task<ServiceResult<IReadOnlyList<SalesOrderHeaderDto>>> GetAllAsync();
    Task<ServiceResult<PagedResultDto<SalesOrderHeaderDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC");
    Task<ServiceResult<Guid>> AddAsync(SalesOrderHeaderDto dto);
    Task<ServiceResult<int>> UpdateAsync(SalesOrderHeaderDto dto);
    Task<ServiceResult<int>> DeleteAsync(Guid uid);
    Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<SalesOrderHeaderDto> batchReq);


   

}
