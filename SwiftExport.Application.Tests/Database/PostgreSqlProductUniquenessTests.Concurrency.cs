using System.Data;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Database;

/// <summary>随机未提交编码的唯一键等待验收；只证明回滚释放后另一插入成功，不冒充提交后冲突。</summary>
public sealed partial class PostgreSqlProductUniquenessIntegrationTests
{
    /// <summary>用服务器阻塞来源及事务锁确认 B 等待 A；A 回滚、B 成功后也回滚 B，全程不提交夹具。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductUniquenessIntegration")]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ConcurrentCodeInsert_ServerWaitThenSuccessAfterRollback(bool master)
    {
        var connectionString = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var first = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        using var second = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        await AssertIndexes(observer.Connection);
        using var transactionA = first.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        using var transactionB = second.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        first.Transaction = transactionA;
        second.Transaction = transactionB;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var audit = Prefix();
        var contextA = Context(first, audit);
        var contextB = Context(second, audit);
        Task? pending = null;
        var rolledBackA = false;
        try
        {
            var code = Prefix() + "Race";
            var entityA = await NewEntity(contextA, master, code);
            var entityB = await NewEntity(contextB, master, code.ToLowerInvariant() + " ");
            // 中性竞争的父型号及颜色在各自事务随机创建且不同，等待只能来自相同编码唯一键。
            await Insert(contextA, entityA);
            var pidA = await first.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: transactionA);
            var pidB = await second.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: transactionB);
            pending = Insert(contextB, entityB, deadline.Token);
            await ObserveWait(observer.Connection, pidA, pidB, pending, deadline.Token);
            Assert.IsFalse(pending.IsCompleted);
            transactionA.Rollback();
            rolledBackA = true;
            first.Transaction = null;
            await pending;
            Assert.AreEqual(1, await second.Connection.ExecuteScalarAsync<int>(
                $"SELECT count(*) FROM public.{Table(master)} WHERE uid=@Uid AND {Column(master)}=@Code COLLATE \"C\"",
                new { entityB.Uid, Code = code.ToLowerInvariant() + " " }, transactionB));
        }
        finally
        {
            // 失败路径也先释放 A，取消并收尾 B 在途命令后才回滚 B，避免事务仍被驱动占用。
            try { if (!rolledBackA) transactionA.Rollback(); }
            finally
            {
                first.Transaction = null;
                deadline.Cancel();
                try { if (pending is not null) await pending; }
                catch (Exception) { /* 主路径保留失败；这里仅观察清理中的任务异常。 */ }
                try { transactionB.Rollback(); }
                finally { second.Transaction = null; }
            }
        }
        await AssertNoFixtures(observer.Connection, audit);
    }

    /// <summary>由独立观察连接刷新统计并读取服务器等待状态，不能用延迟或未完成 Task 单独证明锁竞争。</summary>
    private static async Task ObserveWait(IDbConnection observer, int pidA, int pidB, Task pending, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            await observer.ExecuteAsync(new CommandDefinition("SELECT pg_stat_clear_snapshot()", cancellationToken: token));
            var waiting = await observer.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity a
                    WHERE a.pid=@PidB AND a.state='active' AND a.wait_event_type='Lock'
                      AND @PidA=ANY(pg_blocking_pids(a.pid))
                      AND EXISTS (SELECT 1 FROM pg_locks l
                          WHERE l.pid=a.pid AND NOT l.granted AND l.locktype='transactionid'))
                """, new { PidA = pidA, PidB = pidB }, cancellationToken: token));
            if (waiting) return;
            Assert.IsFalse(pending.IsCompleted, "尚未观测到指定连接的事务锁等待，插入提前结束。");
            await Task.Delay(50, token);
        }
    }
}
