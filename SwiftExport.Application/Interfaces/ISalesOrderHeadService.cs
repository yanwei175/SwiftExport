using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using System;

namespace SwiftExport.Application.Interfaces;

/// <summary>
/// 销售订单应用服务接口。
/// 统一承载：基础 CRUD（继承基类）+ 订单管理页 + 订单详情页。
/// </summary>
public interface ISalesOrderHeadService : IBaseService<SalesOrderHeader, SalesOrderHeaderDto>
{

   

}
