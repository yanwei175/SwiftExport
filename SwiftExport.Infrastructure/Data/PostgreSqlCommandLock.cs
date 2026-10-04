using System.Buffers.Binary;
using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace SwiftExport.Infrastructure.Data;

/// <summary>为命令回执与兼容审计提供同一身份互斥；事务前租约解决高隔离快照问题，不承担业务重试。</summary>
internal static class PostgreSqlCommandLock
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    /// <summary>在同一连接的业务事务开始前获取会话锁；竞争耗尽返回空，获取结果不明时先安全清理再传播异常。</summary>
    internal static async Task<IAsyncDisposable?> AcquireBeforeTransactionAsync(
        DbSession session, Guid commandId, ILogger logger, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (commandId == Guid.Empty) throw new ArgumentException("命令身份不能为空。", nameof(commandId));
        Lease lease;
        lock (session)
        {
            if (session.IsUnusable) throw new ObjectDisposedException(nameof(DbSession), "命令锁会话已隔离。");
            if (session.Transaction is not null || System.Transactions.Transaction.Current is not null)
                throw new InvalidOperationException("命令租约必须在业务事务及环境事务之外获取。");
            if (session.CommandLease is not null)
                throw new InvalidOperationException("同一数据库会话不能嵌套或并行获取命令租约。");
            var connection = session.Connection;
            if (connection.State != ConnectionState.Open)
                throw new InvalidOperationException("命令租约要求已打开且不会更换的连接。");
            if (connection is NpgsqlConnection npgsql)
            {
                var settings = new NpgsqlConnectionStringBuilder(npgsql.ConnectionString);
                if (settings.Multiplexing || settings.NoResetOnClose)
                    throw new NotSupportedException("命令租约不支持 Multiplexing 或 NoResetOnClose。");
            }
            lease = new Lease(session, connection, commandId, logger);
            session.CommandLease = lease;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(Budget);
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                // 发出命令后取消或断线可能无法确定是否已经持锁，不能继续尝试叠加会话锁。
                lease.AttemptPending = true;
                var acquired = await lease.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT pg_try_advisory_lock(@Key);", new { Key = lease.Key },
                    commandTimeout: 10, cancellationToken: deadline.Token));
                lease.AttemptPending = false;
                if (acquired)
                {
                    lease.Held = true;
                    deadline.Token.ThrowIfCancellationRequested();
                    if (!lease.IsCurrent) throw new InvalidOperationException("获取命令租约期间连接生命周期发生变化。");
                    return lease;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(50), deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            await lease.DisposeAsync();
            token.ThrowIfCancellationRequested();
            return null;
        }
        catch
        {
            await lease.DisposeAsync();
            throw;
        }
    }

    /// <summary>按全库 CommandId 获取事务锁；成功返回零，竞争等待十秒返回负一，取消和数据库错误原样传播。</summary>
    internal static async Task<int> AcquireAsync(DbSession session, Guid commandId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (session.IsUnusable) throw new ObjectDisposedException(nameof(DbSession), "命令锁会话已隔离。");
        var transaction = session.Transaction
            ?? throw new InvalidOperationException("命令锁必须在业务事务内获取。");
        if (session.CommandLease is { } lease)
        {
            if (lease.CommandId != commandId || !lease.IsCurrent || !lease.Held
                || !ReferenceEquals(transaction.Connection, lease.Connection))
                throw new InvalidOperationException("事务命令锁不能借用其他身份或已失效的事务前租约。");
            // 租约已经在建立快照之前串行化同一命令，无须重复获取可重入的会话锁。
            return 0;
        }
        // 没有事务前租约时，等待事务锁不能刷新 PG 快照；保留原高隔离拒绝边界。
        if (transaction.IsolationLevel is not (IsolationLevel.ReadCommitted or IsolationLevel.ReadUncommitted))
            throw new NotSupportedException("没有有效事务前命令租约时，PostgreSQL 命令锁仅支持 ReadCommitted。");

        // 固定资源命名及字节顺序，避免进程随机哈希。64位摘要碰撞只会额外串行，不会放过同一身份。
        var key = GetKey(commandId);
        var elapsed = Stopwatch.StartNew();
        var budget = Budget;
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

    /// <summary>保持会话锁与既有事务锁的命名及字节序完全一致。</summary>
    private static long GetKey(Guid commandId) => BinaryPrimitives.ReadInt64BigEndian(
        SHA256.HashData(Encoding.UTF8.GetBytes($"SwiftExport:Command:{commandId:N}")));

    /// <summary>绑定唯一命令与连接周期的租约；异常清理使用独立预算，不能覆盖调用方业务结果。</summary>
    internal sealed class Lease : IAsyncDisposable
    {
        private readonly DbSession _session;
        private readonly ILogger _logger;
        private readonly long _epoch;
        private readonly int? _processId;
        private int _disposed;
        internal IDbConnection Connection { get; }
        internal Guid CommandId { get; }
        internal long Key { get; }
        internal bool Held { get; set; }
        internal bool AttemptPending { get; set; }
        internal bool IsCurrent => Volatile.Read(ref _disposed) == 0
            && ReferenceEquals(_session.CommandLease, this) && _epoch == _session.ConnectionEpoch
            && Connection.State == ConnectionState.Open
            && (_processId is null || Connection is NpgsqlConnection npgsql && npgsql.ProcessID == _processId);

        /// <summary>记录已打开连接的物理身份，不创建连接或事务。</summary>
        internal Lease(DbSession session, IDbConnection connection, Guid commandId, ILogger logger)
        {
            _session = session;
            Connection = connection;
            CommandId = commandId;
            Key = GetKey(commandId);
            _logger = logger;
            _epoch = session.ConnectionEpoch;
            _processId = (connection as NpgsqlConnection)?.ProcessID;
        }

        /// <summary>在业务事务彻底释放后解锁；失败、未知或异常提前释放时丢弃连接，保留原始业务结果。</summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try
            {
                if (!Held && !AttemptPending) return;
                if (_session.Transaction is not null || _epoch != _session.ConnectionEpoch
                    || Connection.State != ConnectionState.Open
                    || _processId is not null && Connection is NpgsqlConnection npgsql && npgsql.ProcessID != _processId)
                    throw new InvalidOperationException("租约释放时事务未清理或连接周期已变化。");
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var released = await Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT pg_advisory_unlock(@Key);", new { Key },
                    commandTimeout: 2, cancellationToken: cleanup.Token));
                if (!released) throw new InvalidOperationException("命令会话锁释放未得到成功确认。");
            }
            catch (Exception ex)
            {
                TryLog(LogLevel.Error, ex, "租约释放无法确认，丢弃数据库连接");
                DiscardConnection();
            }
            finally { ClearReservation(); }
        }

        /// <summary>scope 被提前释放时直接丢弃物理连接，不阻塞等待异步解锁或将带锁连接归还池。</summary>
        internal void Abandon()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            TryLog(LogLevel.Warning, null, "租约随会话提前释放，丢弃数据库连接");
            try { DiscardConnection(); }
            finally { ClearReservation(); }
        }

        /// <summary>先标记逻辑会话不可复用，再清除对应池并关闭连接；清理失败只记录诊断。</summary>
        private void DiscardConnection()
        {
            _session.InvalidateConnection();
            try
            {
                // 普通 Close 会归还池且 reset 延迟到下一次使用，不能保证及时解除会话锁。
                if (Connection is NpgsqlConnection npgsql) NpgsqlConnection.ClearPool(npgsql);
            }
            catch (Exception ex) { TryLog(LogLevel.Critical, ex, "连接池隔离失败"); }
            try { Connection.Dispose(); }
            catch (Exception ex) { TryLog(LogLevel.Critical, ex, "连接丢弃失败"); }
        }

        /// <summary>日志失败不能阻止连接丢弃或覆盖调用方结果；不构造共享日志框架。</summary>
        private void TryLog(LogLevel level, Exception? exception, string phase)
        {
            try { _logger.Log(level, exception, "命令 {CommandId} {Phase}。", CommandId, phase); }
            catch { /* 清理必须继续，不递归调用已失效的日志提供者。 */ }
        }

        /// <summary>只移除本租约的占位，重复释放不能覆盖后续租约。</summary>
        private void ClearReservation()
        {
            lock (_session)
            {
                if (ReferenceEquals(_session.CommandLease, this)) _session.CommandLease = null;
            }
        }
    }
}
