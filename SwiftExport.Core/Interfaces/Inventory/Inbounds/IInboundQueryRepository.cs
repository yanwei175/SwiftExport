using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Inventory.Inbounds;

/// <summary>入库单据查询持久化边界。</summary>
public interface IInboundQueryRepository
{
    /// <summary>分页查询单据摘要。</summary>
    Task<PagedResponseBase<InboundManagerListItemDto>> SearchAsync(InboundManagerSearchRequest request, CancellationToken token);
    /// <summary>读取完整单据；不存在返回空。</summary>
    Task<InboundManagerDetailDto?> GetDetailAsync(Guid uid, CancellationToken token);
}
