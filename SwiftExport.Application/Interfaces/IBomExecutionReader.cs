using SwiftExport.Contracts.DTOs.Products.Execution;

namespace SwiftExport.Application.Interfaces;

/// <summary>在调用方事务内完整读取当前结构，失败时不提供部分执行快照。</summary>
public interface IBomExecutionReader
{
    /// <summary>按根产品稳定身份读取完整依据；必须已有事务，无效结构抛出中文业务异常。</summary>
    Task<BomExecutionSnapshotDto> ReadAsync(Guid rootProductId, CancellationToken cancellationToken = default);
}
