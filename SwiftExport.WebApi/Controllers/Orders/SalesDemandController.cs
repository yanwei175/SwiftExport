using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.WebApi.Controllers.Base;

namespace SwiftExport.WebApi.Controllers.Orders;

/// <summary>
/// 销售毛需求控制器。
/// </summary>
public class IndependentDemandController(IIndependentDemandServices service)
    : BaseController<IndependentDemand, IndependentDemandDto>(service)
{
    /// <summary>
    /// 按来源单据触发销售毛需求拆解。
    /// 说明：审批通过后的销售需求由该接口驱动拆解进入需求池。
    /// </summary>
    [HttpPost("decompose-by-source")]
    public async Task<ServiceResult<List<DependentDemandDto>>> ExecuteDecomposeBySourceAsync([FromBody] IndependentDemandDecomposeRequestDto request)
    {
        request ??= new IndependentDemandDecomposeRequestDto();
        return await service.ExecuteDecomposeBySourceAsync(request.SourceType, request.SourceNo);
    }
}
