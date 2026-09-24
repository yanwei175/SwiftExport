using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Inventory.Outbounds;

/// <summary>读取出库摘要、头行详情和人工草稿所需产品资料。</summary>
public interface IOutboundQueryRepository
{
    /// <summary>分页读取出库单摘要。</summary>
    Task<PagedResponseBase<OutboundManagerListItemDto>> SearchAsync(OutboundManagerSearchRequest request, CancellationToken token);
    /// <summary>读取完整单据详情。</summary>
    Task<OutboundManagerDetailDto?> GetDetailAsync(Guid uid, CancellationToken token);
    /// <summary>按仓库分页读取可选择的正库存产品。</summary>
    Task<PagedResponseBase<OutboundItemCandidateDto>> SearchItemsAsync(OutboundItemSearchRequest request, CancellationToken token);
    /// <summary>读取草稿行使用的产品身份与单位。</summary>
    Task<IReadOnlyList<OutboundItemCandidateDto>> GetProductsAsync(IReadOnlyList<Guid> ids, CancellationToken token);
    /// <summary>判断产品是否允许新增业务引用。</summary>
    Task<bool> IsProductActiveAsync(Guid productId, CancellationToken token);
}
