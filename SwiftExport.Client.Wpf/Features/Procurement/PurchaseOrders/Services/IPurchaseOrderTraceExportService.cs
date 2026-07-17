using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

public interface IPurchaseOrderTraceExportService
{
    Task<string?> ExportAsync(PurchaseOrderTraceDto trace, CancellationToken cancellationToken = default);
}
