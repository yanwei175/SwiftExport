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
using SwiftExport.Infrastructure.Data;
using System.Text.Json;

using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
namespace SwiftExport.Application.Interfaces.SalesOrders.Confirmation;

/// <summary>SalesOrderCancellationService 的窄业务契约。</summary>
public interface ISalesOrderCancellationService
{
    /// <summary>按现行审计重放协议取消确认。</summary>
    Task<ServiceResult<SalesOrderConfirmationResultDto>> CancelConfirmationAsync(Guid uid, CancelSalesOrderConfirmationCommand request);
}
