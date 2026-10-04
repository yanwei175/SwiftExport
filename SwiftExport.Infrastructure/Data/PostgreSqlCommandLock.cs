using System.Buffers.Binary;
using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Dapper;

namespace SwiftExport.Infrastructure.Data;

/// <summary>为命令回执与兼容审计查询提供同一事务级命令互斥；不拥有事务，不承担业务重试。</summary>
internal static class PostgreSqlCommandLock
{
    /// <summary>按全库 CommandId 获取事务锁；成功返回零，竞争等待十秒返回负一，取消和数据库错误原样传播。</summary>
    internal static async Task<int> AcquireAsync(DbSession session, Guid commandId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var transaction = session.Transaction
            ?? throw new InvalidOperationException("命令锁必须在业务事务内获取。");
        // PG 的事务快照不会因等到 advisory lock 而刷新；切换相关用例前必须另行解决旧快照问题。
        if (transaction.IsolationLevel is not (IsolationLevel.ReadCommitted or IsolationLevel.ReadUncommitted))
            throw new NotSupportedException("PostgreSQL 命令锁当前仅支持 ReadCommitted；更高隔离级别须先完成快照一致性适配。");

        // 固定资源命名及字节顺序，避免进程随机哈希。64位摘要碰撞只会额外串行，不会放过同一身份。
        var resource = Encoding.UTF8.GetBytes($"SwiftExport:Command:{commandId:N}");
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(resource));
        var elapsed = Stopwatch.StartNew();
        var budget = TimeSpan.FromSeconds(10);
        const string sql = "SELECT pg_try_advisory_xact_lock(@Key);";
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (elapsed.Elapsed >= budget) return -1;
            if (await session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(sql,
                new { Key = key }, transaction, commandTimeout: 10, cancellationToken: token)))
                return 0;

            // 只轮询竞争结果，不重试异常；锁随调用方提交或回滚释放，不改连接级超时配置。
            var remaining = budget - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero) return -1;
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(50)
                ? remaining : TimeSpan.FromMilliseconds(50), token);
        }
    }
}
