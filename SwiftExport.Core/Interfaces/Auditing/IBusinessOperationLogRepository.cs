using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Core.Entities.Auditing;

namespace SwiftExport.Core.Interfaces.Auditing;

/// <summary>定义业务操作日志的幂等读取、追加写入和聚合追溯边界。</summary>
public interface IBusinessOperationLogRepository
{
    /// <summary>在当前事务内读取某模块占用同一命令标识的日志，用于跨操作幂等冲突判断。</summary>
    Task<BusinessOperationLog?> GetByModuleCommandAsync(string module, Guid commandId);
    Task<BusinessOperationLog?> GetByCommandAsync(string module, string operationType, Guid commandId);
    Task<int> InsertAsync(BusinessOperationLog log);
    Task<IReadOnlyList<BusinessOperationTraceItemDto>> GetByAggregateAsync(string module, Guid aggregateUid, CancellationToken cancellationToken = default);
}
