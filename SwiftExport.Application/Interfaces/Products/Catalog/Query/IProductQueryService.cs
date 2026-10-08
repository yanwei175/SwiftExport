using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Products.Catalog.Query;

/// <summary>提供产品详情、SKU、分页和选择器的只读查询。</summary>
public interface IProductQueryService
{
    /// <summary>读取指定产品；缺失返回原有未找到契约。</summary>
    Task<ServiceResult<ProductDto>> GetByIdAsync(Guid uid);
    /// <summary>读取产品分页投影。</summary>
    Task<ServiceResult<PagedResponseBase<ProductDto>>> GetPagedAsync(ProductSearchRequest request);
    /// <summary>按业务 SKU 读取产品投影。</summary>
    Task<ServiceResult<ProductDto>> GetBySkuAsync(string sku);
    /// <summary>搜索产品选择器投影。</summary>
    Task<ServiceResult<List<ProductLookupDto>>> SearchLookupAsync(ProductLookupRequest request, CancellationToken cancellationToken = default);
    /// <summary>读取产品关联客户编码列表。</summary>
    Task<ServiceResult<IReadOnlyList<string>>> GetAllCustomerCodeListAsync();
    /// <summary>按产品模块访问权限分页读取活动默认仓库候选，不授予库存模块查询权限。</summary>
    Task<ServiceResult<PagedResponseBase<ProductDefaultWarehouseCandidateDto>>> GetDefaultWarehouseCandidatesAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default);
}
