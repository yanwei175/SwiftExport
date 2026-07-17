using SwiftExport.Contracts.DTOs.Inventory;

namespace SwiftExport.Core.Interfaces.Inventory;

public interface IInboundQueryRepository
{
    Task<InboundTraceDto?> GetTraceAsync(Guid inboundDocumentId, CancellationToken cancellationToken = default);
}
