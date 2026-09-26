using SwiftExport.Core.Models.Procurement;

namespace SwiftExport.Core.Interfaces.Procurement;

/// <summary>确认、反向及资格预览共享的完整来源到入库事实锁序。</summary>
public interface IProcurementOrderFactLockRepository
{
    /// <summary>按来源、物料、引用、采购、入库锁序读取，并复核发现阶段的引用集合。</summary>
    Task<ProcurementReversalFacts> LockAsync(Guid uid, CancellationToken ct);
}
