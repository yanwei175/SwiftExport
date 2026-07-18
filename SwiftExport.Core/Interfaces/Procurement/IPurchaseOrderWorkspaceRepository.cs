using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Procurement;

namespace SwiftExport.Core.Interfaces.Procurement;

public interface IPurchaseOrderWorkspaceRepository
{
    Task<PagedResponseBase<PurchaseOrderListItemDto>> SearchAsync(PurchaseOrderSearchRequest request, CancellationToken cancellationToken = default);
    Task<PurchaseOrderHead?> GetHeadAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<PurchaseOrderListItemDto?> GetHeadSummaryAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PurchaseOrderLineDetailDto>> GetLineDetailsAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PurchaseOrderSourceContributionDto>> GetContributionsAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PurchaseOrderSourceContributionDto>> GetContributionsForUpdateAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<PurchaseOrderHead?> GetHeadForUpdateAsync(Guid uid);
    Task<IReadOnlyList<PurchaseOrderLine>> GetLinesForUpdateAsync(Guid uid);
    Task<bool> IsActiveSupplierAsync(Guid supplierId);
    Task<bool> HasDownstreamFactsAsync(Guid uid);
    Task<int> UpdateDraftAsync(PurchaseOrderHead head, IReadOnlyList<PurchaseOrderLine> lines, int expectedVersion);
    Task<int> SetConfirmationAsync(Guid uid, int expectedVersion, bool confirmed, DateTime? confirmedDate, string? confirmedBy, string updatedBy);
}
