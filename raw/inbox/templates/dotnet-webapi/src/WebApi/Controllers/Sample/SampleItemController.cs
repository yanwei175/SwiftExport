using Microsoft.AspNetCore.Mvc;
using {{ProjectName}}.Application.Interfaces;
using {{ProjectName}}.Contracts.DTOs.Sample;
using {{ProjectName}}.Contracts.Wrappers;
using {{ProjectName}}.WebApi.Filters;

namespace {{ProjectName}}.WebApi.Controllers.Sample;

[ApiController]
[Route("api/[controller]")]
public class SampleItemController(ISampleItemService service) : ControllerBase
{
    [HttpGet("{id}")]
    public Task<ServiceResult<SampleItemDto>> GetById(Guid id)
        => service.GetByIdAsync(id);

    [HttpPost("page")]
    public Task<ServiceResult<PagedResultDto<SampleItemDto>>> SearchPage([FromBody] PagedRequestDto request)
        => service.SearchPageAsync(request);

    [HttpGet("code/{code}")]
    public Task<SampleItemDto?> GetByCode(string code)
        => service.GetByCodeAsync(code);

    [HttpPost]
    [PreventDuplicateRequest]
    public Task<ServiceResult<Guid>> Create([FromBody] SampleItemDto dto)
        => service.CreateAsync(dto);

    [HttpPut("{id}")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<int>> Update(Guid id, [FromBody] SampleItemDto dto)
        => service.UpdateAsync(id, dto);

    [HttpDelete("{id}")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<int>> Delete(Guid id)
        => service.DeleteAsync(id);

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<BatchResultDto>> BatchSave([FromBody] BatchRequestDto<SampleItemDto> request)
        => service.BatchSaveAsync(request);
}
