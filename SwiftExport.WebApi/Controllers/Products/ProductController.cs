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


public class ProductController(IProductService service) : BaseController<Product, ProductDto>(service)
{

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<ProductDto>>> GetPaged([FromBody] ProductSearchRequest request) => await service.GetPagedAsync(request);

    [HttpPost("lookup")]
    public async Task<ServiceResult<List<ProductLookupDto>>> SearchLookup([FromBody] ProductLookupRequest request) =>
        await service.SearchLookupAsync(request);

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<ProductBatchSaveResponse>> BatchSave([FromBody] ProductBatchSaveRequest req) => await service.BatchSaveAsync(req);

    [HttpPost("category-assignment/page")]
    public async Task<ServiceResult<PagedResponseBase<ProductCategoryAssignmentRowDto>>> GetCategoryAssignmentPage(
        [FromBody] ProductCategoryAssignmentSearchRequest request,
        CancellationToken cancellationToken) =>
        await service.GetCategoryAssignmentPageAsync(request, cancellationToken);

    [HttpPost("assign-category")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<AssignProductsToCategoryResult>> AssignCategory(
        [FromBody] AssignProductsToCategoryRequest request,
        CancellationToken cancellationToken) =>
        await service.AssignCategoryAsync(request, cancellationToken);

    // ================= 查询类 (GET) =================

    // 1. 获取全部 (标准 RESTful 应该直接叫 api/product，靠 GET 区分)
    // 前端路由: ApiRouteBuilder.Resource("product")
    [HttpGet("all")] // 为了和分页区分，这里加个 /all 是可以接受的
    public async Task<ServiceResult<IReadOnlyList<ProductDto>>> GetAll()
    {
        return ServiceResult<IReadOnlyList<ProductDto>>.Fail("数据太多,不允许GetAll");
    }


    // 3. 补充功能：获取代码列表 (子资源)
    [HttpGet("customerCodes")] // 名字稍微精简一点
    public async Task<ServiceResult<IReadOnlyList<string>>> GetAllCustomerCodeList()
    {
        return await service.GetAllCustomerCodeListAsync();
    }

    // 5. 根据 SKU 获取 (特例查询)
    [HttpGet("sku/{sku}")]
    public async Task<ServiceResult<ProductDto>> GetBySku(string sku)
    {
        return await service.GetBySkuAsync(sku);
    }

    // ================= 操作类 (POST / PUT / DELETE) =================







}
