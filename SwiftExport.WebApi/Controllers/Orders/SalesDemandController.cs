using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Independent;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.Orders;

/// <summary>
/// 销售毛需求控制器。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class IndependentDemandController(IIndependentDemandServices service)
    : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ServiceResult<IndependentDemandDto>> GetById(Guid id) => await service.GetByIdAsync(id);

    [HttpPost]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<Guid>> Create([FromBody] IndependentDemandDto dto) => await service.AddAsync(dto);

    [HttpPut("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Update(Guid id, [FromBody] IndependentDemandDto dto) => await service.UpdateAsync(dto);

    [HttpDelete("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Delete(Guid id) => await service.DeleteAsync(id);

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResultDto<IndependentDemandDto>>> GetPagedList([FromBody] QueryCriteria criteria, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 500)
        => await service.GetPagedListAsync(criteria, pageIndex, pageSize);

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<BatchResultDto>> BatchSave([FromBody] BatchRequestDto<IndependentDemandDto> req) => await service.BatchSaveAsync(req);

    /// <summary>
    /// 按来源单据触发销售毛需求拆解。
    /// 说明：审批通过后的销售需求由该接口驱动拆解进入需求池。
    /// </summary>
    [HttpPost("decompose-by-source")]
    public async Task<ServiceResult<List<IndependentDemandDto>>> ExecuteDecomposeBySourceAsync([FromBody] IndependentDemandDecomposeRequestDto request)
    {
        request ??= new IndependentDemandDecomposeRequestDto();
        return await service.根据来源单据拆解至净需求Async(request.SourceType, request.SourceNo);
    }
}
