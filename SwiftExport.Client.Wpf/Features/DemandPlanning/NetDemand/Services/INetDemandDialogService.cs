using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Services;

public interface INetDemandDialogService
{
    Task<bool> ShowPurchaseDraftPreviewAsync(
        PurchaseDraftPreviewOpenRequest request,
        CancellationToken cancellationToken = default);
}
