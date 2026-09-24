using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Shipping;

namespace SwiftExport.Core.Interfaces.Shipping.PackingLists;

/// <summary>在调用方事务中锁定及保存装箱单头行，不拥有事务提交权。</summary>
public interface IPackingListDocumentRepository
{
    /// <summary>检查装箱单结构是否已就绪。</summary>
    Task<bool> IsSchemaReadyAsync(CancellationToken token = default);
    /// <summary>在取得锁前读取当前头行，用于发现来源集合。</summary>
    Task<PackingListDetailDto?> GetDetailAsync(Guid uid, CancellationToken token = default);
    /// <summary>锁定拼箱身份并检查跨单占用。</summary>
    Task<bool> HasForeignPackingGroupsAsync(Guid headId, IReadOnlyCollection<Guid> groupIds, CancellationToken token = default);
    /// <summary>锁定单头，可包含软删除历史。</summary>
    Task<PackingListHead?> LockHeaderAsync(Guid uid, bool includeDeleted, CancellationToken token = default);
    /// <summary>锁定有效明细。</summary>
    Task<IReadOnlyList<PackingListLine>> LockLinesAsync(Guid uid, CancellationToken token = default);
    /// <summary>锁定行身份并读取已有归属。</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> LockLineOwnersAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken token = default);
    /// <summary>保存头行和显式删除行，检查预期版本。</summary>
    Task SaveAsync(PackingListHead header, IReadOnlyList<PackingListLine> activeLines,
        IReadOnlyList<PackingListLine> deletedLines, int expectedVersion, CancellationToken token = default);
    /// <summary>软删除未过账草稿及有效明细。</summary>
    Task DeleteAsync(PackingListHead header, CancellationToken token = default);
}
