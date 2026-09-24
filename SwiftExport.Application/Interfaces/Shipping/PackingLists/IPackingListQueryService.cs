using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Shipping.PackingLists;

/// <summary>读取装箱单管理、详情及来源候选。</summary>
public interface IPackingListQueryService
{
    /// <summary>分页读取管理摘要。</summary>
    Task<ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>> SearchAsync(PackingListManagerSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>读取完整单据快照。</summary>
    Task<ServiceResult<PackingListDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default);
    /// <summary>分页读取同客户可交付来源。</summary>
    Task<ServiceResult<PagedResponseBase<PackingListSourceCandidateDto>>> SearchSourcesAsync(PackingListSourceSearchRequest request, CancellationToken cancellationToken = default);
}

