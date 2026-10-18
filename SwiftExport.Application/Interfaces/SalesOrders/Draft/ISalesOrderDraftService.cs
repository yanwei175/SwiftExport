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
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using System.Text.Json;

using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
namespace SwiftExport.Application.Interfaces.SalesOrders.Draft;

/// <summary>SalesOrderDraftService 的窄业务契约。</summary>
public interface ISalesOrderDraftService
{
    /// <summary>保存销售草稿及全部行变更。</summary>
    Task<ServiceResult<SaveSalesOrderResponse>> SaveAsync(SaveSalesOrderRequest request);
    /// <summary>删除未确认且未生成成品需求的草稿。</summary>
    Task<ServiceResult<bool>> DeleteDraftAsync(Guid uid, SalesOrderCommandRequest request);
}
