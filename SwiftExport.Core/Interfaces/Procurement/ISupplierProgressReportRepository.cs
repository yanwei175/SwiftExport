using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Core.Entities.Procurement;

namespace SwiftExport.Core.Interfaces.Procurement;

public interface ISupplierProgressReportRepository
{
    Task<SupplierProgressReport?> GetByCommandAsync(Guid commandId);
    Task<SupplierProgressReport?> GetLatestForUpdateAsync(Guid purchaseOrderLineId);
    Task<IReadOnlyList<SupplierProgressReportDto>> GetByPurchaseOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default);
    Task<int> InsertAsync(SupplierProgressReport report);
}
