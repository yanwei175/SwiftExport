using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.RequestProtection;
namespace SwiftExport.WebApi.Controllers.Products;


public class ProductCategoryController(IProductCategoryService service) : BaseController<ProductCategory, ProductCategoryDto>(service)
{

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<ProductCategoryDto>>> GetPaged([FromBody] ProductCategorySearchRequest request) => await service.GetPagedAsync(request);

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<ProductCategoryBatchSaveResponse>> BatchSave([FromBody] ProductCategoryBatchSaveRequest request) => await service.BatchSaveAsync(request);


    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<ProductCategoryDto>>> GetAll()
    {
        return await service.GetAllAsync();
    }






}
