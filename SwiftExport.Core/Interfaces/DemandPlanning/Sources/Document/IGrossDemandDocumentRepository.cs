using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document;

/// <summary>在调用方事务中锁定与写入来源需求本体及退出状态。</summary>
public interface IGrossDemandDocumentRepository
{
    /// <summary>在调用方事务内锁定未删除的来源需求。</summary>
    Task<GrossDemand?> GetByIdForUpdateAsync(Guid uid);
    /// <summary>按完整来源身份稳定排序并锁定活动需求行。</summary>
    Task<IReadOnlyList<GrossDemand>> GetActiveBySourceForUpdateAsync(string sourceType, Guid sourceUid);
    /// <summary>写入新来源需求并返回各行持久化结果。</summary>
    Task<IReadOnlyList<RepositoryWriteResult>> AddSourceDemandsAsync(IEnumerable<GrossDemand> demands);
    /// <summary>按版本更新上游来源行事实，不改变拆解历史。</summary>
    Task<RepositoryWriteResult> UpdateSourceDemandAsync(GrossDemand demand, int expectedVersion);
    /// <summary>按版本更新手工来源需求内容。</summary>
    Task<RepositoryWriteResult> UpdateManualAsync(GrossDemand demand, int expectedVersion);
    /// <summary>按版本关闭手工来源需求并记录原因。</summary>
    Task<RepositoryWriteResult> CloseManualAsync(Guid uid, int expectedVersion, string reason);
    /// <summary>删除已经由物料历史校验证明从未建立过物料事实的来源需求。</summary>
    Task<int> HardDeleteForSourceCancellationAsync(IEnumerable<Guid> grossDemandIds);
    /// <summary>将已回退物料历史的来源需求置为作废。</summary>
    Task<int> VoidRolledBackForSourceCancellationAsync(IEnumerable<Guid> grossDemandIds, string reason);
    /// <summary>按完整来源身份作废仍活动的来源需求。</summary>
    Task<int> VoidPendingBySourceAsync(string sourceType, Guid sourceUid, string reason);
}
