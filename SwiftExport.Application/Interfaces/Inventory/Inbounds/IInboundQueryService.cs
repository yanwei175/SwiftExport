using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory.Inbounds;

/// <summary>提供入库管理列表与单据详情的只读用例。</summary>
public interface IInboundQueryService
{
    /// <summary>按查询条件分页读取入库管理摘要。</summary>
    Task<ServiceResult<PagedResponseBase<InboundManagerListItemDto>>> SearchAsync(InboundManagerSearchRequest request, CancellationToken token = default);
    /// <summary>按单据身份读取完整入库详情。</summary>
    Task<ServiceResult<InboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token = default);
}
