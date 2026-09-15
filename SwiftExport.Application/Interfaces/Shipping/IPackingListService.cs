using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Shipping;

/// <summary>提供装箱单管理查询、选源和草稿原子命令。</summary>
public interface IPackingListService
{
    /// <summary>分页读取管理摘要。</summary>
    Task<ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>> SearchAsync(PackingListManagerSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>读取完整单据快照。</summary>
    Task<ServiceResult<PackingListDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default);
    /// <summary>分页读取同客户可交付来源。</summary>
    Task<ServiceResult<PagedResponseBase<PackingListSourceCandidateDto>>> SearchSourcesAsync(PackingListSourceSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>原子创建装箱单草稿。</summary>
    Task<ServiceResult<PackingListDraftCommandResult>> CreateAsync(CreatePackingListDraftCommand command, CancellationToken cancellationToken = default);
    /// <summary>按头行版本原子更新草稿。</summary>
    Task<ServiceResult<PackingListDraftCommandResult>> UpdateAsync(Guid uid, UpdatePackingListDraftCommand command, CancellationToken cancellationToken = default);
    /// <summary>按版本软删除活动未过账草稿。</summary>
    Task<ServiceResult<PackingListDraftCommandResult>> DeleteAsync(Guid uid, DeletePackingListDraftCommand command, CancellationToken cancellationToken = default);
}
