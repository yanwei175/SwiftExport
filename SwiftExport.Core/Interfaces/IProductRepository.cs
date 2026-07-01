using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Core.Interfaces;

public interface IProductRepository : IRepository<Product>, IWhereQueryRepository<Product>, IObjectQueryRepository<Product>
{
    Task<IReadOnlyList<string>> GetAllCustomerCodeListAsync();
    Task<Product?> GetBySkuAsync(string sku);
    /// <summary>
    /// 按 SKU 列表批量查询产品。
    /// </summary>
    Task<IReadOnlyList<Product>> GetBySkuListAsync(IEnumerable<string> skuList);
    Task<PagedResult<ProductDto>> GetPagedProductDtoAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "CreatedDate DESC");
    Task<IReadOnlyList<string>> GetExistingSkuListAsync(IEnumerable<string> skuList);
}

