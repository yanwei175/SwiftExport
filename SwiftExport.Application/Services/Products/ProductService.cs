using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
namespace SwiftExport.Application.Services.Products;

public partial class ProductService(IProductRepository productRepository, ServiceBaseContext context, IProductCategoryRepository categoryRepository)
    : BaseService<Product, ProductDto>(productRepository, context), IProductService
{
    private readonly ILogger<ProductService> _logger = context.LoggerFactory.CreateLogger<ProductService>();


    public async Task<ServiceResult<IReadOnlyList<ProductDto>>> GetAllAsync()
    {
        return ServiceResult<IReadOnlyList<ProductDto>>.Fail("数据太多,不允许GetAll");
    }

    public async Task<ServiceResult<IReadOnlyList<string>>> GetAllCustomerCodeListAsync()
    {
        // 使用特有的 _customerProductRepository 调用特有方法
        var list = await productRepository.GetAllCustomerCodeListAsync();
        if (list == null || list.Count == 0)
            return ServiceResult<IReadOnlyList<string>>.Fail("空的客户代码集合");

        return ServiceResult<IReadOnlyList<string>>.Ok(list);
    }

    // 实现特有的业务逻辑：根据 SKU 查询
    public async Task<ServiceResult<ProductDto>> GetBySkuAsync(string sku)
    {
        var entity = await productRepository.GetBySkuAsync(sku);
        if (entity == null)
            return ServiceResult<ProductDto>.Fail($"未找到 SKU 为 {sku} 的产品");

        return ServiceResult<ProductDto>.Ok(entity.Adapt<ProductDto>());
    }

    public async Task<ServiceResult<PagedResponseBase<ProductDto>>> GetPagedAsync(ProductSearchRequest request)
    {
        try
        {
            var result = await productRepository.GetPagedAsync(request);
            if (result == null)
                return ServiceResult<PagedResponseBase<ProductDto>>.Fail("未找到数据");
            return ServiceResult<PagedResponseBase<ProductDto>>.Ok(result);

        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResponseBase<ProductDto>>.Fail(ex.Message);
        }

    }

    public async Task<ServiceResult<List<ProductLookupDto>>> SearchLookupAsync(ProductLookupRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Keyword))
        {
            return ServiceResult<List<ProductLookupDto>>.Ok([]);
        }

        var items = await productRepository.SearchLookupAsync(request);
        return ServiceResult<List<ProductLookupDto>>.Ok(items.ToList());
    }



    public async Task<ServiceResult<ProductBatchSaveResponse>> BatchSaveAsync(ProductBatchSaveRequest batchReq)
    {
        throw new NotImplementedException();
    }

}
