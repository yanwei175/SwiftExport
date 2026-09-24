using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory.Outbounds;

/// <summary>提供出库列表、详情和选品的只读用例。</summary>
public interface IOutboundQueryService
{
    /// <summary>按筛选条件读取出库单列表。</summary>
    Task<ServiceResult<PagedResponseBase<OutboundManagerListItemDto>>> SearchAsync(OutboundManagerSearchRequest request, CancellationToken token = default);
    /// <summary>读取一张出库单的头行详情。</summary>
    Task<ServiceResult<OutboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token = default);
    /// <summary>按仓库和排除条件读取可选产品。</summary>
    Task<ServiceResult<PagedResponseBase<OutboundItemCandidateDto>>> SearchItemsAsync(OutboundItemSearchRequest request, CancellationToken token = default);
}
