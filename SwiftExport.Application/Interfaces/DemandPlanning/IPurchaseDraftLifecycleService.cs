using SwiftExport.Contracts.DTOs.PurchaseOrders;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.DemandPlanning;

public interface IPurchaseDraftLifecycleService
{
    Task<ServiceResult<PurchaseDraftLifecycleResult>> CancelAsync(
        Guid uid,
        CancelPurchaseDraftCommand command,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PurchaseDraftReleasePreviewResponse>> PreviewReleaseAsync(
        Guid uid,
        PurchaseDraftReleasePreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PurchaseDraftLifecycleResult>> ReleaseAsync(
        Guid uid,
        ReleasePurchaseDraftCommand command,
        CancellationToken cancellationToken = default);
}
