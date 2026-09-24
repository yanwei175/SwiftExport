using SwiftExport.Core.Entities.Commands;

namespace SwiftExport.Core.Interfaces.Commands;

/// <summary>在调用方事务内锁定命令身份，并保存可重放的成功回执。</summary>
public interface ICommandReceiptRepository
{
    /// <summary>取得全库统一的事务级命令锁；负值表示锁竞争或失败。</summary>
    Task<int> LockAsync(Guid commandId, CancellationToken token);
    /// <summary>读取已提交的原始回执，不通过审计日志恢复业务结果。</summary>
    Task<CommandReceipt?> FindAsync(Guid commandId, CancellationToken token);
    /// <summary>在业务写入所在事务中追加成功回执。</summary>
    Task InsertAsync(CommandReceipt receipt, CancellationToken token);
}
