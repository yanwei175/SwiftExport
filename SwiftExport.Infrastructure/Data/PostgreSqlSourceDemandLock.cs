using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace SwiftExport.Infrastructure.Data;

/// <summary>在需求业务事务建立快照前准备全局命令项、SourceUID范围及Gross UID身份锁；仅管理同连接租约，不执行业务或重试事务。</summary>
internal static class PostgreSqlSourceDemandLock
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    /// <summary>先全部S再全部U，去重并稳定排序；竞争耗尽返回空，取消/未知获取结果安全释放或隔离连接后原样传播。</summary>
    internal static async Task<IAsyncDisposable?> AcquireBeforeTransactionAsync(DbSession session,
        IReadOnlyCollection<Guid> sourceUids, IReadOnlyCollection<Guid> grossUids, ILogger logger, CancellationToken token)
        => await AcquireMaterialBeforeTransactionAsync(session, [], sourceUids, grossUids, logger, token);

    /// <summary>全局CommandItem先于全部S及全部U；空Guid仍参与写入身份保护，复用唯一来源租约与原30秒预算。</summary>
    internal static async Task<IAsyncDisposable?> AcquireMaterialBeforeTransactionAsync(DbSession session,
        IReadOnlyCollection<Guid> commandItemIds, IReadOnlyCollection<Guid> sourceUids,
        IReadOnlyCollection<Guid> grossUids, ILogger logger, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(commandItemIds);
        ArgumentNullException.ThrowIfNull(sourceUids); ArgumentNullException.ThrowIfNull(grossUids);
        token.ThrowIfCancellationRequested();
        // SourceType不参与分区：同SourceUID所有类型额外串行，避免SQL字典等值字符串落不同锁。
        var keys = commandItemIds.Select(uid => Key("CommandItem", uid)).Distinct().Order()
            .Concat(sourceUids.Select(uid => Key("Source", uid)).Distinct().Order())
            .Concat(grossUids.Select(uid => Key("Gross", uid)).Distinct().Order()).Distinct().ToArray();
        Lease lease;
        lock (session)
        {
            if (session.IsUnusable) throw new ObjectDisposedException(nameof(DbSession), "来源锁会话已隔离。");
            if (session.Transaction is not null || System.Transactions.Transaction.Current is not null)
                throw new InvalidOperationException("来源租约必须在业务事务及环境事务之外获取。");
            if (session.SourceDemandLease is not null)
                throw new InvalidOperationException("同一会话不能嵌套或并行准备来源租约。");
            if (session.CommandLease is { } command && (!command.IsCurrent || !command.Held || command.AttemptPending))
                throw new InvalidOperationException("需求资源准备只能借用已完成且仍有效的事务前命令租约。");
            if (session.Connection is not NpgsqlConnection connection || connection.State != ConnectionState.Open)
                throw new NotSupportedException("来源租约要求当前会话的原生、已打开Npgsql连接。");
            var settings = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
            if (settings.Multiplexing || settings.NoResetOnClose)
                throw new NotSupportedException("来源租约不支持Multiplexing或NoResetOnClose。");
            lease = new Lease(session, connection, keys, logger);
            session.SourceDemandLease = lease;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(Budget);
        try
        {
            foreach (var key in keys)
            {
                while (true)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    lease.AttemptPending = true;
                    var acquired = await lease.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                        "SELECT pg_try_advisory_lock(@Key)", new { Key = key }, commandTimeout: 30, cancellationToken: deadline.Token));
                    lease.AttemptPending = false;
                    if (acquired)
                    {
                        lease.Held.Add(key);
                        if (!lease.IsCurrent) throw new InvalidOperationException("来源准备期间连接生命周期发生变化。");
                        break;
                    }
                    await Task.Delay(50, deadline.Token);
                }
            }
            deadline.Token.ThrowIfCancellationRequested();
            return lease;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && deadline.IsCancellationRequested)
        { await lease.DisposeAsync(); token.ThrowIfCancellationRequested(); return null; }
        catch { await lease.DisposeAsync(); throw; }
    }

    /// <summary>固定资源族及Guid字节文本，摘要碰撞仅额外串行；允许空Guid作为原仓储缺失身份的真实锁键。</summary>
    internal static long Key(string family, Guid uid) => BinaryPrimitives.ReadInt64BigEndian(
        SHA256.HashData(Encoding.UTF8.GetBytes($"SwiftExport:SourceDemand:{family}:{uid:N}")));

    /// <summary>多资源只获取一次、逆序释放；同命令租约可并存，但不能跨连接周期或提前归还带锁连接。</summary>
    internal sealed class Lease : IAsyncDisposable
    {
        private readonly DbSession _session;
        private readonly ILogger _logger;
        private readonly long _epoch;
        private readonly int _pid;
        private int _disposed;
        internal NpgsqlConnection Connection { get; }
        internal long[] Keys { get; }
        internal List<long> Held { get; } = [];
        internal bool AttemptPending { get; set; }
        internal bool IsCurrent => Volatile.Read(ref _disposed) == 0 && ReferenceEquals(_session.SourceDemandLease, this)
            && _epoch == _session.ConnectionEpoch && Connection.State == ConnectionState.Open && Connection.ProcessID == _pid;

        /// <summary>只有本轮仍绑定物理连接且资源已成功获取，才能供真实仓储借用；占位及未知获取结果不能当作保护。</summary>
        internal bool Covers(string family, Guid uid) => IsCurrent && !AttemptPending && Held.Contains(Key(family, uid));

        /// <summary>绑定当前物理连接与完整资源集合，不新建连接或事务。</summary>
        internal Lease(DbSession session, NpgsqlConnection connection, long[] keys, ILogger logger)
        { _session = session; Connection = connection; Keys = keys; _logger = logger; _epoch = session.ConnectionEpoch; _pid = connection.ProcessID; }

        /// <summary>事务完全释放后解锁；获取结果未知、连接变更或解锁失败则隔离物理连接，清理不覆盖业务异常。</summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try
            {
                if (AttemptPending) throw new InvalidOperationException("来源会话锁获取结果尚未确认。");
                if (Held.Count == 0) return;
                if (_session.Transaction is not null || _epoch != _session.ConnectionEpoch
                    || Connection.State != ConnectionState.Open || Connection.ProcessID != _pid)
                    throw new InvalidOperationException("来源租约释放时事务未清理或物理连接已变化。");
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                for (var index = Held.Count - 1; index >= 0; index--)
                {
                    var unlocked = await Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                        "SELECT pg_advisory_unlock(@Key)", new { Key = Held[index] }, commandTimeout: 2, cancellationToken: cleanup.Token));
                    if (!unlocked) throw new InvalidOperationException("来源会话锁释放未得到成功确认。");
                }
            }
            catch (Exception ex) { Log(ex, "来源租约释放无法确认，丢弃数据库连接"); DiscardConnection(); }
            finally { ClearReservation(); }
        }

        /// <summary>scope提前关闭时不等待异步解锁，直接丢弃持锁物理连接；迟到的重复Dispose无副作用。</summary>
        internal void Abandon()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { DiscardConnection(); }
            finally { ClearReservation(); }
        }

        /// <summary>先禁止逻辑会话复用，再清除池并关闭物理连接；不依赖延迟reset解除锁。</summary>
        private void DiscardConnection()
        {
            _session.InvalidateConnection();
            try { NpgsqlConnection.ClearPool(Connection); } catch (Exception ex) { Log(ex, "来源连接池隔离失败"); }
            try { Connection.Dispose(); } catch (Exception ex) { Log(ex, "来源连接关闭失败"); }
        }

        /// <summary>诊断不含凭据，日志失败不得中断连接清理。</summary>
        private void Log(Exception exception, string phase)
        { try { _logger.LogError(exception, "{Phase}。来源资源数{Count}。", phase, Keys.Length); } catch { /* 必须继续物理隔离。 */ } }
        /// <summary>只移除本轮占位，不能清掉后续租约。</summary>
        private void ClearReservation()
        { lock (_session) { if (ReferenceEquals(_session.SourceDemandLease, this)) _session.SourceDemandLease = null; } }
    }
}
