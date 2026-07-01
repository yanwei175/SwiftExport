using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces;

public interface IPackingListLineRepository : IRepository<PackingListLine>, IBatchWriteRepository<PackingListLine>
{
    Task<IReadOnlyList<PackingListLine>> GetByPackingListNoAsync(string packingListNo);
}
