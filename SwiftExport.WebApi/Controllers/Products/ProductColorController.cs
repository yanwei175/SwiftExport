using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Filters;

using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
namespace SwiftExport.WebApi.Controllers.Products;

[ApiController]
[Route("api/[controller]")]
public class ProductColorController(IProductColorService service) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ServiceResult<ProductColorDto>> GetById(Guid id) => await service.GetByIdAsync(id);

    [HttpPost]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<Guid>> Create([FromBody] ProductColorDto dto) => await service.AddAsync(dto);

    [HttpPut("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Update(Guid id, [FromBody] ProductColorDto dto) => await service.UpdateAsync(dto);

    [HttpDelete("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Delete(Guid id) => await service.DeleteAsync(id);

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResultDto<ProductColorDto>>> GetPagedList([FromBody] ProductColorSearchRequest request)
        => await service.GetPagedAsync(request);

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<ProductColorBatchSaveResponse>> BatchSave([FromBody] ProductColorBatchSaveRequest req) => await service.BatchSaveAsync(req);

    [HttpGet]
    public async Task<ServiceResult<PagedResultDto<ProductColorDto>>> GetPaged([FromQuery] ProductColorSearchRequest request)
    {
        return await service.GetPagedAsync(request);
    }

    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<ProductColorDto>>> GetAll()
    {
        return await service.GetAllAsync();
    }


}
