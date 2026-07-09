using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.Filters;
namespace SwiftExport.WebApi.Controllers.Products;


public class ProductColorController(IProductColorService service) : BaseController<ProductColor, ProductColorDto>(service)
{


    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<ProductColorBatchSaveResponse>> BatchSave([FromBody] ProductColorBatchSaveRequest request) => await service.BatchSaveAsync(request);

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<ProductColorDto>>> GetPaged([FromQuery] ProductColorSearchRequest request) => await service.GetPagedAsync(request);

    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<ProductColorDto>>> GetAll()
    {
        return await service.GetAllAsync();
    }


}
