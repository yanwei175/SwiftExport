using SwiftExport.Core.Models.Procurement;
using SwiftExport.Contracts.DTOs.Errors;

namespace SwiftExport.Core.Interfaces.Procurement;

/// <summary>确认、反向及资格预览共享的完整来源到入库事实锁序。</summary>
public interface IProcurementOrderFactLockRepository
{
    /// <summary>按来源、物料、引用、采购、入库锁序读取并复核集合；成功返回完整事实，冲突返回错误且不暴露部分事实。</summary>
    Task<(ProcurementReversalFacts? Facts, ServiceErrorDto? Error)> LockAsync(Guid uid, CancellationToken ct);

    /// <summary>追加关联先合并已有关联与待选物料的来源集合，再按同一锁序读取；不读取或阻断收货下游。</summary>
    Task<(ProcurementReversalFacts? Facts, ServiceErrorDto? Error)> LockForAssociationAsync(Guid uid, IReadOnlyCollection<Guid> netDemandIds, CancellationToken ct);
}
