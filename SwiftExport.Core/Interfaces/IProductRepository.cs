using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.ReadModels.Products;

namespace SwiftExport.Core.Interfaces;

public interface IProductRepository : IRepository<Product>
{
    Task<IReadOnlyList<string>> GetAllCustomerCodeListAsync();
    Task<Product?> GetBySkuAsync(string sku);
    Task<PagedResult<ProductPagedItem>> GetPagedReadModelAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Id DESC");
    Task<IReadOnlyList<string>> GetExistingSkuListAsync(IEnumerable<string> skuList);
}

