using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Core.Entities.Auditing;

namespace SwiftExport.Core.Interfaces.Auditing;

public interface IBusinessOperationLogRepository
{
    Task<BusinessOperationLog?> GetByCommandAsync(string module, string operationType, Guid commandId);
    Task<int> InsertAsync(BusinessOperationLog log);
    Task<IReadOnlyList<BusinessOperationTraceItemDto>> GetByAggregateAsync(string module, Guid aggregateUid, CancellationToken cancellationToken = default);
}
