using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces;

public interface IProductRepository : IRepository<Product>
{
    Task<IReadOnlyList<string>> GetAllCustomerCodeListAsync();
    Task<Product> GetBySkuAsync(string sku);
}
