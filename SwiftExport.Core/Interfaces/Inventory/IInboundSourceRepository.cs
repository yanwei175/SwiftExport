using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Inventory;

public interface IInboundSourceRepository
{
    Task<PagedResponseBase<InboundSourceDto>> SearchAsync(InboundSourceSearchRequest request, CancellationToken cancellationToken = default);
    Task<InboundSourceDto?> GetForUpdateAsync(InboundSourceTypeEnum sourceType, Guid sourceUid);
    Task<decimal> GetReceivedNetQtyAsync(InboundSourceTypeEnum sourceType, Guid sourceUid, Guid? excludeInboundDocumentId = null);
    Task<bool> HasDownstreamOutboundAsync(Guid inboundDocumentId);
}
