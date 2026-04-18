using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.ReadModels.Products;

namespace SwiftExport.Core.Interfaces;

public interface ICustomerProductRepository : IRepository<CustomerProduct>
{
    Task<IReadOnlyList<int>> GetAllCustomerIdListAsync();  
    Task<CustomerProduct?> GetByCustomerSkuAsync(string customerSku, int customerId);
    Task<IReadOnlyList<string>> GetExistingCustomerSkuListAsync(IEnumerable<string> skuList, int customerId);
}

