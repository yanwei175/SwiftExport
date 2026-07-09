using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Orders;

/// <summary>
/// 销售订单行数据源服务。
/// 提供“CustomerProduct + Product 联查”的分页查询能力，
/// 为订单行编辑界面提供可直接回填的基础数据。
/// </summary>
public class SalesOrderLineService(ServiceBaseContext context,
    ISalesOrderLineRepository salesOrderLineRepo,
    IDocumentNoService documentNoService)
    : BaseService<SalesOrderLine, SalesOrderLineDto>(salesOrderLineRepo, context), ISalesOrderLineService
{

    private readonly ISalesOrderLineRepository _salesOrderLineRepo = salesOrderLineRepo;
    private readonly IDocumentNoService _docService = documentNoService;
    private ILogger? _loggerInstance;
    protected ILogger Logger => _loggerInstance ??= context.LoggerFactory.CreateLogger(GetType());


    public async Task<ServiceResult<List<SalesOrderLineDto>>> GetLineListByHeadUidAsync(Guid headUid)
    {
        try
        {
            var rst = await _salesOrderLineRepo.GetLinesByOrderIdAsync(headUid);
            if (rst == null)
            {
                return ServiceResult<List<SalesOrderLineDto>>.Fail("未找到相关数据");
            }
            return ServiceResult<List<SalesOrderLineDto>>.Ok(rst.Adapt<List<SalesOrderLineDto>>());
        }
        catch (Exception ex)
        {
            return ServiceResult<List<SalesOrderLineDto>>.Fail(ex.Message);
        }
    }
}
