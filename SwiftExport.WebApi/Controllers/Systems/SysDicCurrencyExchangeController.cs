using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.Systems;

/// <summary>
/// 货币汇率控制器。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SysDicCurrencyExchangeController(ISysDicCurrencyExchangeService service) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ServiceResult<SysDicCurrencyExchangeDto>> GetById(Guid id) => await service.GetByIdAsync(id);

    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<SysDicCurrencyExchangeDto>>> GetAll() => await service.GetAllAsync();

    [HttpPost]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<Guid>> Create([FromBody] SysDicCurrencyExchangeDto dto) => await service.AddAsync(dto);

    [HttpPut("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Update(Guid id, [FromBody] SysDicCurrencyExchangeDto dto) => await service.UpdateAsync(dto);

    [HttpDelete("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Delete(Guid id) => await service.DeleteAsync(id);

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResultDto<SysDicCurrencyExchangeDto>>> GetPagedList([FromBody] QueryCriteria criteria, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 500)
        => await service.GetPagedListAsync(criteria, pageIndex, pageSize);

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<BatchResultDto>> BatchSave([FromBody] BatchRequestDto<SysDicCurrencyExchangeDto> req) => await service.BatchSaveAsync(req);
}
