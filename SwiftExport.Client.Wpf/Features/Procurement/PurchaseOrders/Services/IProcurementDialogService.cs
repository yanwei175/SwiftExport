using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

public interface IProcurementDialogService
{
    Task<IReadOnlyList<NetDemandRowModel>> SelectNetDemandsAsync(CancellationToken cancellationToken = default);
}
