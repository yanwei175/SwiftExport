using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Orders;

/// <summary>
/// 毛需求控制器。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class InDependentDemandController(IIndependentDemandServices service) : ControllerBase
{
    /// <summary>
    /// 分页查询净需求。
    /// </summary>
    [HttpPost("search")]
    public async Task<ServiceResult<PagedResultDto<IndependentDemandDto>>> GetPagedListAsync([FromBody] InDependSearchReq search)
    {
        return await service.GetPagedListAsync(search);
    }

    /// <summary>
    /// 按选中需求池ID列表转采购。
    /// </summary>
    [HttpPost("convert-to-do/by-ids")]
    public async Task<ServiceResult<List<IndependentDemandDto>>> CoverToDependByIdList([FromBody] List<int> req)
    {
        return await service.根据选定毛需求拆解至净需求Async(req);
    }

    /// <summary>
    /// 批量回退草稿（将选中需求池ID列表的毛需求状态回退到草稿，适用于误操作或需要重新拆解的场景）
    /// </summary>
    /// <param name="orderIds">请求回退Id列表</param>
    /// <returns>回退成功列表</returns>
    [HttpPost("batch-rollback")]
    public async Task<ServiceResult<List<IndependentDemandDto>>> BatchRollBackAsync([FromBody] List<int> orderIds)
    {
        return await service.批量回退草稿(orderIds);
    }

}
