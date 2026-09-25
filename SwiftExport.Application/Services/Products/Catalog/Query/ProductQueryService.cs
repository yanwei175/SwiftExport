using SwiftExport.Core.Interfaces.Products.Catalog;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Products.Catalog.Query;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Products.Catalog.Query;

/// <summary>读取产品详情和选择器投影，不开启写入事务。</summary>
public sealed class ProductQueryService(IProductRepository repository, ServiceBaseContext context) : IProductQueryService
{
    private readonly ILogger<ProductQueryService> _logger = context.LoggerFactory.CreateLogger<ProductQueryService>();

    /// <inheritdoc />
    public async Task<ServiceResult<ProductDto>> GetByIdAsync(Guid uid)
    {
        try
        {
            var entity = await repository.GetByIdAsync(uid);
            return entity is null
                ? ServiceResult<ProductDto>.Fail(ServiceErrorCodes.NotFound, "未找到数据")
                : ServiceResult<ProductDto>.Ok(entity.Adapt<ProductDto>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<ProductDto>.Fail(ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PagedResponseBase<ProductDto>>> GetPagedAsync(ProductSearchRequest request)
    {
        try
        {
            var result = await repository.GetPagedAsync(request);
            return result is null
                ? ServiceResult<PagedResponseBase<ProductDto>>.Fail("未找到数据")
                : ServiceResult<PagedResponseBase<ProductDto>>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<PagedResponseBase<ProductDto>>.Fail(ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ProductDto>> GetBySkuAsync(string sku)
    {
        var dto = await repository.GetDtoBySkuAsync(sku);
        return dto is null
            ? ServiceResult<ProductDto>.Fail($"未找到 SKU 为 {sku} 的产品")
            : ServiceResult<ProductDto>.Ok(dto);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<List<ProductLookupDto>>> SearchLookupAsync(ProductLookupRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Keyword))
            return ServiceResult<List<ProductLookupDto>>.Ok([]);
        var items = await repository.SearchLookupAsync(request);
        return ServiceResult<List<ProductLookupDto>>.Ok(items.ToList());
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<string>>> GetAllCustomerCodeListAsync()
    {
        var list = await repository.GetAllCustomerCodeListAsync();
        return list is null || list.Count == 0
            ? ServiceResult<IReadOnlyList<string>>.Fail("空的客户代码集合")
            : ServiceResult<IReadOnlyList<string>>.Ok(list);
    }
}
