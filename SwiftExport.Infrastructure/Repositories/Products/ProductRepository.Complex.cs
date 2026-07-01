using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Infrastructure.Repositories.Products;

public partial class ProductRepository
{
    public Task<IReadOnlyList<Product>> GetListAsync(string whereSql, object? param = null, bool withUpdLock = false)
    {
        return GetListCoreAsync(whereSql, param, withUpdLock);
    }

    public Task<IReadOnlyList<Product>> GetListAsync(object criteriaObject, bool withUpdLock = false)
    {
        return GetListCoreAsync(criteriaObject, withUpdLock);
    }
}
