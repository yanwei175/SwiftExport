using SwiftExport.Core.Models.Procurement;
using SwiftExport.Contracts.DTOs.Errors;

namespace SwiftExport.Core.Interfaces.Procurement;

/// <summary>确认、反向及资格预览共享的完整来源到入库事实锁序。</summary>
public interface IProcurementOrderFactLockRepository
{
    /// <summary>按来源、物料、引用、采购、入库锁序读取并复核集合；成功返回完整事实，冲突返回错误且不暴露部分事实。</summary>
    Task<(ProcurementReversalFacts? Facts, ServiceErrorDto? Error)> LockAsync(Guid uid, CancellationToken ct);
}
