using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Core.Interfaces.Inventory;

public interface IOutboundDocumentRepository
{
    Task<PagedResponseBase<OutboundListItemDto>> SearchAsync(OutboundSearchRequest request, CancellationToken cancellationToken = default);
    Task<OutboundHeaderDto?> GetHeaderAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutboundLineDto>> GetLinesAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryLedgerItemDto>> GetLedgersAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<OutboundDocument?> GetByCommandForUpdateAsync(Guid commandId);
    Task<IReadOnlyList<OutboundDocument>> GetByPackingListForUpdateAsync(Guid packingListId);
    Task<IReadOnlyList<OutboundDocumentLine>> GetLinesForUpdateAsync(Guid documentId);
    Task<int> InsertAsync(OutboundDocument document, IReadOnlyList<OutboundDocumentLine> lines);
    Task<int> SetReversedAsync(Guid uid, int expectedVersion, DateTime reversedDate, string reversedBy, string reason);
}
