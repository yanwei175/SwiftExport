using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Models;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Exceptions;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Data;
using System.Text.Json;

using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
namespace SwiftExport.Application.Interfaces.Orders.SalesOrders.Query;

/// <summary>SalesOrderQueryService 的窄业务契约。</summary>
public interface ISalesOrderQueryService
{
    /// <summary>查询管理列表。</summary>
    Task<ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>> SearchAsync(SalesOrderManagerSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>查询客户代码筛选建议。</summary>
    Task<ServiceResult<List<string>>> GetCustomerCodesAsync(CancellationToken cancellationToken = default);
    /// <summary>读取完整销售文档。</summary>
    Task<ServiceResult<SalesOrderDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default);
    /// <summary>查询销售产品候选。</summary>
    Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>> SearchProductsAsync(SalesOrderProductSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>查询销售客户候选。</summary>
    Task<ServiceResult<PagedResponseBase<SalesOrderCustomerLookupDto>>> SearchCustomersAsync(SalesOrderCustomerSearchRequest request, CancellationToken cancellationToken = default);
}
