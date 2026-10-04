namespace SwiftExport.Core.Interfaces.Commands;

/// <summary>可选命令准备能力：在最外层业务事务建立快照之前取得身份互斥，不开启或提交业务事务。</summary>
public interface ICommandTransactionPreparation
{
    /// <summary>事务外取得命令互斥租约；竞争耗尽返回空，取消及获取失败原样传播。调用方须在业务事务释放后释放租约。</summary>
    /// <remarks>租约清理不得覆盖已确定的业务结果或原始异常；无法确认释放时，实现必须隔离不可复用的连接并记录诊断。</remarks>
    Task<IAsyncDisposable?> AcquireBeforeTransactionAsync(Guid commandId, CancellationToken token);
}
