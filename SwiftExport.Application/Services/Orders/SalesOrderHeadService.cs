using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Orders;

/// <summary>
/// 销售订单应用服务实现。
/// </summary>
public partial class SalesOrderHeadService(
    ISalesOrderHeadRepository headRepo,
    ISalesOrderLineRepository lineRepo,
    ICustomerRepository customerRepository,
    ISalesOrderLineService salesOrderLineService,
    IDocumentNoService documentNoService,
    IIndependentDemandRepository salesDemandRepository,
    ServiceBaseContext context)
    : ApplicationServiceBase(context), ISalesOrderHeadService
{
    private readonly ServiceCrud<SalesOrderHeader, SalesOrderHeaderDto> _crud = new(headRepo, context);
    private readonly ISalesOrderHeadRepository _headRepo = headRepo;
    private readonly ISalesOrderLineRepository _lineRepo = lineRepo;
    private readonly ICustomerRepository _customerRepository = customerRepository;
    private readonly ISalesOrderLineService _salesOrderLineService = salesOrderLineService;
    private readonly IDocumentNoService _documentNoService = documentNoService;
    private readonly IIndependentDemandRepository _demandRepo = salesDemandRepository;





}
