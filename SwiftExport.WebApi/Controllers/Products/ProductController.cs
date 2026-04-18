using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.WebApi.Controllers.Base;

namespace SwiftExport.WebApi.Controllers.Products;

public class ProductController(IProductService service) : BaseController<Product, ProductDto>(service)
{

    // ================= 查询类 (GET) =================

    // 1. 获取全部 (标准 RESTful 应该直接叫 api/product，靠 GET 区分)
    // 前端路由: ApiRouteBuilder.Resource("product")
    [HttpGet("all")] // 为了和分页区分，这里加个 /all 是可以接受的
    public async Task<ServiceResult<IReadOnlyList<ProductDto>>> GetAll()
    {
        return ServiceResult<IReadOnlyList<ProductDto>>.Fail("数据太多,不允许GetAll");
    }

    // 2. 分页搜索产品
    // 由于查询条件中包含 List，使用 FromQuery 传参有些 HTTP 客户端容易拼错。因此改用 POST 查询
    [HttpPost("search")]
    public async Task<ServiceResult<PagedResultDto<ProductDto>>> GetProductPagedList([FromBody] ProductSearchDto search)
    {
        return await service.GetPagedListAsync(search);
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