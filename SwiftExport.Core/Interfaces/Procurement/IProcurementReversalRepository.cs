using SwiftExport.Core.Models.Procurement;
using SwiftExport.Contracts.SysEnums;
namespace SwiftExport.Core.Interfaces.Procurement;
/// <summary>采购反向的锁定读取与原子写入端口，事务由 Application 所有。</summary>
public interface IProcurementReversalRepository
{
    /// <summary>按来源、物料、引用、采购、入库锁序读取，并复核发现阶段的引用集合。</summary>
    Task<ProcurementReversalFacts> LockAsync(Guid uid, CancellationToken ct);
    /// <summary>持久化已验证的反向操作；释放指定行引用并记录审计，异常由调用方回滚。</summary>
    Task ApplyAsync(ProcurementReversalFacts facts, ProcurementReversalOperation operation, IReadOnlyCollection<Guid> lineIds, string reason, CancellationToken ct);
}
