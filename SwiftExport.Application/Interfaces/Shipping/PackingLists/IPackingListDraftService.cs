using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Shipping.PackingLists;

/// <summary>执行装箱单草稿创建、更新和软删除。</summary>
public interface IPackingListDraftService
{
    /// <summary>在同一草稿事务中分配来源并拆行，不生成交付或库存事实。</summary>
    Task<ServiceResult<PackingListDraftCommandResult>> AssociateSourcesAsync(Guid uid, Guid lineUid, AssociatePackingListSourcesCommand command, CancellationToken token = default);
    /// <summary>原子创建装箱单草稿。</summary>
    Task<ServiceResult<PackingListDraftCommandResult>> CreateAsync(CreatePackingListDraftCommand command, CancellationToken cancellationToken = default);
    /// <summary>按头行版本原子更新草稿。</summary>
    Task<ServiceResult<PackingListDraftCommandResult>> UpdateAsync(Guid uid, UpdatePackingListDraftCommand command, CancellationToken cancellationToken = default);
    /// <summary>按版本软删除活动未过账草稿。</summary>
    Task<ServiceResult<PackingListDraftCommandResult>> DeleteAsync(Guid uid, DeletePackingListDraftCommand command, CancellationToken cancellationToken = default);
}

