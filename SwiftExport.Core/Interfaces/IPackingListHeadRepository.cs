using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces;

public interface IPackingListHeadRepository : IRepository<PackingListHead>
{
    Task<PackingListHead?> GetByPackingListNoAsync(string packingListNo);
}
