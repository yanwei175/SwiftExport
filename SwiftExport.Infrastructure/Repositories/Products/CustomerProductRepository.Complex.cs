using SwiftExport.Core.Entities.Relations;

namespace SwiftExport.Infrastructure.Repositories.Products;

public partial class CustomerProductRepository
{
    public Task<IReadOnlyList<Guid>> AddRangeAsync(IEnumerable<CustomerProduct> entities)
    {
        return AddRangeCoreAsync(entities);
    }

    public Task<int> UpdateRangeAsync(IEnumerable<CustomerProduct> entities)
    {
        return UpdateRangeCoreAsync(entities);
    }

    public Task<int> DeleteRangeAsync(IEnumerable<Guid> uidList, bool softDelete = true)
    {
        return DeleteRangeCoreAsync(uidList, softDelete);
    }

    public Task<int> DeleteRangeSoftAsync(IEnumerable<Guid> uidList)
    {
        return DeleteRangeSoftCoreAsync(uidList);
    }
}
