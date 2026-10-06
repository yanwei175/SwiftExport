namespace SwiftExport.Core.Interfaces.Commands;

/// <summary>为已拥有外层命令租约的自动子命令补齐事务前身份互斥；只用于实际多命令调用链，不开启或提交业务事务。</summary>
public interface ICommandRelatedTransactionPreparation
{
    /// <summary>在同一连接、外层租约及首次业务事务之前取得明确子命令的锁；返回与外层共用的幂等释放租约，超时返回空，取消/错误原样传播。</summary>
    Task<IAsyncDisposable?> AcquireRelatedBeforeTransactionAsync(Guid commandId, IReadOnlyList<Guid> relatedCommandIds, CancellationToken token);
}
