using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Core.Interfaces.Inventory;

public interface IInboundDocumentRepository
{
    Task<PagedResponseBase<InboundListItemDto>> SearchAsync(InboundSearchRequest request, CancellationToken cancellationToken = default);
    Task<InboundHeaderDto?> GetHeaderAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InboundLineDto>> GetLinesAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<InboundDocument?> GetForUpdateAsync(Guid uid);
    Task<IReadOnlyList<InboundDocumentLine>> GetLinesForUpdateAsync(Guid uid);
    Task<InboundDocument?> GetByCommandForUpdateAsync(Guid commandId);
    Task<int> InsertAsync(InboundDocument document, IReadOnlyList<InboundDocumentLine> lines);
    Task<int> UpdateDraftAsync(InboundDocument document, IReadOnlyList<InboundDocumentLine> lines, int expectedVersion);
    Task<int> SetPostedAsync(Guid uid, int expectedVersion, DateTime postedDate, string postedBy, Guid commandId);
    Task<int> SetReversedAsync(Guid uid, int expectedVersion, DateTime reversedDate, string reversedBy, string reason, Guid commandId);
    Task<IReadOnlyList<InventoryLedgerItemDto>> GetLedgersAsync(Guid uid, CancellationToken cancellationToken = default);
}
