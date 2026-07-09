using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
namespace SwiftExport.Core.Interfaces;

public interface IProductRepository : IRepository<Product>
{
    Task<IReadOnlyList<string>> GetAllCustomerCodeListAsync();
    Task<Product?> GetBySkuAsync(string sku);
    /// <summary>
    /// 按 SKU 列表批量查询产品。
    /// </summary>
    Task<IReadOnlyList<Product>> GetBySkuListAsync(IEnumerable<string> skuList);
    Task<PagedResponseBase<ProductDto>> GetPagedAsync(ProductSearchRequest request);
    Task<IReadOnlyList<string>> GetExistingSkuListAsync(IEnumerable<string> skuList);
}

