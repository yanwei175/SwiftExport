using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces;

public interface ISalesOrderLineRepository : IRepository<SalesOrderLine>
{
    Task<IReadOnlyList<SalesOrderLine>> GetLinesByOrderIdAsync(Guid orderId, bool withUpdLock = false);
    Task<int> PrepareLineNumbersAsync(Guid orderId);
    Task<IReadOnlyList<RepositoryWriteResult>> AddWorkspaceLinesAsync(IEnumerable<SalesOrderLine> lines);
    Task<IReadOnlyList<RepositoryWriteResult>> UpdateWorkspaceLinesAsync(IEnumerable<SalesOrderLine> lines);
    Task<IReadOnlyList<RepositoryWriteResult>> DeleteWorkspaceLinesAsync(IEnumerable<SalesOrderDeletedLineDto> lines);
    Task<int> DeleteByOrderIdAsync(Guid orderId);
}
