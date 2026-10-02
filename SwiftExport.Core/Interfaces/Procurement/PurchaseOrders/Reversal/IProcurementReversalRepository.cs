using SwiftExport.Core.Models.Procurement;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.SysEnums;
namespace SwiftExport.Core.Interfaces.Procurement;
/// <summary>采购反向的锁定读取与原子写入端口，事务由 Application 所有。</summary>
public interface IProcurementReversalRepository : IProcurementOrderFactLockRepository
{
    /// <summary>持久化已验证的反向操作；任一步失败即停止并返回结果，由调用方回滚已完成的写入。</summary>
    Task<RepositoryWriteResult> ApplyAsync(ProcurementReversalFacts facts, ProcurementReversalOperation operation, IReadOnlyCollection<Guid> lineIds, string reason, CancellationToken ct);
}
