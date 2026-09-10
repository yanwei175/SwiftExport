using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

public interface IProcurementDialogService
{
    Task<IReadOnlyList<MaterialDemandWorkspaceRowModel>> SelectNetDemandsAsync(CancellationToken cancellationToken = default);
}
