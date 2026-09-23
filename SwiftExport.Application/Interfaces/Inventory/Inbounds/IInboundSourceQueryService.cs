using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory.Inbounds;

/// <summary>提供采购入库来源候选行的只读查询。</summary>
public interface IInboundSourceQueryService
{
    /// <summary>按采购来源条件分页选源，不预占收货数量。</summary>
    Task<ServiceResult<PagedResponseBase<InboundSourceCandidateDto>>> SearchSourcesAsync(InboundSourceSearchRequest request, CancellationToken token = default);
}
