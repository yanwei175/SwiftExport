using System.Data;
using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Commands;
using SwiftExport.Infrastructure.Repositories.SalesOrders.Document;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>来源事务前租约真实连接验收；反射访问内部能力以避免新增运行公开接口，不冒充Gross仓储/完整业务接入。</summary>
[TestClass, DoNotParallelize, TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlSourceDemandPreparationIntegrationTests
{
    private static readonly MethodInfo AcquireMethod = typeof(DbSession).Assembly
        .GetType("SwiftExport.Infrastructure.Data.PostgreSqlSourceDemandLock", true)!
        .GetMethod("AcquireBeforeTransactionAsync", BindingFlags.Static | BindingFlags.NonPublic)!;

    /// <summary>同来源UID不同Gross身份必须互斥；独立来源同时可获取，去重避免可重入计数导致锁残留。</summary>
    [TestMethod]
    public async Task SameSourceSerializes_DifferentSourceRemainsIndependent()
    {
        using var first = Session(); using var second = Session(); using var independent = Session(); using var observer = Session();
        var source = Guid.NewGuid(); var gross = Guid.NewGuid();
        IAsyncDisposable? held = null; IAsyncDisposable? other = null;
        Task<IAsyncDisposable?>? pending = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            held = await Acquire(first, [source, source], [gross, gross]); Assert.IsNotNull(held);
            Assert.AreEqual(2L, await Locks(observer, first));
            other = await Acquire(independent, [Guid.NewGuid()], [Guid.NewGuid()], deadline.Token); Assert.IsNotNull(other);
            Assert.AreEqual(2L, await Locks(observer, independent));
            pending = Acquire(second, [source], [Guid.NewGuid()], deadline.Token);
            await ObserveAttempt(observer, second, deadline.Token);
            Assert.IsFalse(pending.IsCompleted); Assert.AreEqual(0L, await Locks(observer, second));
            await held.DisposeAsync();
            var acquired = await pending; Assert.IsNotNull(acquired); Assert.AreEqual(2L, await Locks(observer, second));
            await acquired.DisposeAsync(); await acquired.DisposeAsync();
            Assert.AreEqual(0L, await Locks(observer, second)); Assert.AreEqual(1, await second.Connection.ExecuteScalarAsync<int>("SELECT 1"));
        }
        finally
        {
            deadline.Cancel(); if (held is not null) await held.DisposeAsync();
            try { if (pending is not null && await pending is { } late) await late.DisposeAsync(); }
            catch (OperationCanceledException) { /* 取消等待必须已清理部分锁或隔离连接。 */ }
            finally { if (other is not null) await other.DisposeAsync(); }
        }
    }

    /// <summary>等待U时已取得S，取消后S不得残留；未知获取结果允许隔离连接，不能带锁归还池。</summary>
    [TestMethod]
    public async Task PartialAcquisitionCancellation_ReleasesOrDiscards()
    {
        using var holder = Session(); using var waiter = Session(); using var observer = Session();
        var uid = Guid.NewGuid(); var pid = ((NpgsqlConnection)waiter.Connection).ProcessID;
        var held = await Acquire(holder, [], [uid]); Assert.IsNotNull(held);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<IAsyncDisposable?>? pending = null;
        try
        {
            pending = Acquire(waiter, [Guid.NewGuid()], [uid], cancellation.Token);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (await Locks(observer, pid) != 1) await Task.Delay(25, deadline.Token);
            cancellation.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
            Assert.AreEqual(0L, await Locks(observer, pid));
            if (waiter.Connection.State == ConnectionState.Open)
                Assert.AreEqual(1, await waiter.Connection.ExecuteScalarAsync<int>("SELECT 1"));
            else
                await Assert.ThrowsAsync<ObjectDisposedException>(() => Acquire(waiter, [], []));
        }
        finally
        {
            cancellation.Cancel(); await held.DisposeAsync();
            if (pending is not null) { try { if (await pending is { } late) await late.DisposeAsync(); } catch (OperationCanceledException) { } }
        }
    }

    /// <summary>来源租约与同CommandId租约独立占位，逆序释放来源不能提前释放命令锁。</summary>
    [TestMethod]
    public async Task CommandAndSourceLeases_CoexistWithoutReentrantLeak()
    {
        using var session = Session(); using var observer = Session();
        var receipts = new PostgreSqlCommandReceiptRepository(Context(session));
        await using var command = await receipts.AcquireBeforeTransactionAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.IsNotNull(command);
        var source = await Acquire(session, [Guid.Empty], [Guid.Empty]); Assert.IsNotNull(source);
        try
        {
            Assert.AreEqual(3L, await Locks(observer, session));
            await source.DisposeAsync(); Assert.AreEqual(1L, await Locks(observer, session));
            await command.DisposeAsync(); Assert.AreEqual(0L, await Locks(observer, session));
            Assert.AreEqual(1, await session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
        }
        finally { await source.DisposeAsync(); }
    }

    /// <summary>拒绝事务中和嵌套获取，不改变父事务；正常释放后允许下一轮新租约。</summary>
    [TestMethod]
    public async Task InvalidPreparationBoundary_LeavesCallerTransactionIntact()
    {
        using var session = Session(); using var observer = Session();
        Begin(session, IsolationLevel.ReadCommitted); var tx = session.Transaction;
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Acquire(session, [Guid.NewGuid()], []));
            Assert.AreSame(tx, session.Transaction);
            Assert.AreEqual(1, await session.Connection.ExecuteScalarAsync<int>("SELECT 1", transaction: tx));
        }
        finally { Rollback(session); }
        var lease = await Acquire(session, [Guid.NewGuid()], []); Assert.IsNotNull(lease);
        try { await Assert.ThrowsAsync<InvalidOperationException>(() => Acquire(session, [], [])); }
        finally { await lease.DisposeAsync(); }
        await using var again = await Acquire(session, [], []); Assert.IsNotNull(again);
        Assert.AreEqual(0L, await Locks(observer, session));
    }

    /// <summary>scope提前Dispose直接关闭持锁物理连接；迟到重复解锁无副作用。</summary>
    [TestMethod]
    public async Task SessionDisposal_DoesNotReturnHeldLocksToPool()
    {
        using var session = Session(); using var observer = Session();
        var pid = ((NpgsqlConnection)session.Connection).ProcessID;
        var lease = await Acquire(session, [Guid.NewGuid()], [Guid.NewGuid()]); Assert.IsNotNull(lease);
        Assert.AreEqual(2L, await Locks(observer, pid));
        session.Dispose(); await lease.DisposeAsync(); session.Dispose();
        await ObserveNoLocks(observer, pid);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => Acquire(session, [], []));
    }

    /// <summary>连接Close/Open周期使旧租约失效，即使池再次返回同PID也必须隔离，不复用旧准备身份。</summary>
    [TestMethod]
    public async Task ConnectionCycle_InvalidatesSourceLease()
    {
        using var session = Session(); using var observer = Session();
        var pid = ((NpgsqlConnection)session.Connection).ProcessID;
        var lease = await Acquire(session, [Guid.NewGuid()], []); Assert.IsNotNull(lease);
        session.Connection.Close(); session.Connection.Open();
        await lease.DisposeAsync(); await ObserveNoLocks(observer, pid);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => Acquire(session, [], []));
    }

    /// <summary>实际来源销售头变更：事务前等待后再开原高隔离读取新版本，而已有旧快照仍旧；非完整确认/来源接收验收。</summary>
    [TestMethod]
    [DataRow(IsolationLevel.RepeatableRead)]
    [DataRow(IsolationLevel.Serializable)]
    public async Task HighIsolation_FirstSnapshotStartsAfterSourcePreparation(IsolationLevel isolation)
    {
        using var holder = Session(); using var waiter = Session(); using var oldReader = Session();
        var heads = new PostgreSqlSalesOrderHeadRepository(Context(holder));
        var head = new SalesOrderHeader { OrderNo = "PGSP" + Guid.NewGuid().ToString("N") };
        IAsyncDisposable? held = null; Task<IAsyncDisposable?>? pending = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            Begin(holder, IsolationLevel.ReadCommitted); Assert.IsTrue((await heads.AddAsync(head)).Success); Commit(holder);
            held = await Acquire(holder, [head.Uid], []); Assert.IsNotNull(held);
            pending = Acquire(waiter, [head.Uid], [], deadline.Token);
            await ObserveAttempt(oldReader, waiter, deadline.Token); Assert.IsFalse(pending.IsCompleted);
            Begin(oldReader, IsolationLevel.RepeatableRead);
            var before = await new PostgreSqlSalesOrderHeadRepository(Context(oldReader)).GetByIdAsync(head.Uid);
            Assert.IsNotNull(before); Assert.AreEqual(1, before.Version);
            Begin(holder, IsolationLevel.ReadCommitted);
            var update = await heads.GetByIdAsync(head.Uid, true); Assert.IsNotNull(update);
            update.Remark = "来源事务前互斥后的版本"; Assert.IsTrue((await heads.UpdateAsync(update)).Success); Commit(holder);
            await held.DisposeAsync();
            var prepared = await pending; Assert.IsNotNull(prepared);
            Begin(waiter, isolation);
            var latest = await new PostgreSqlSalesOrderHeadRepository(Context(waiter)).GetByIdAsync(head.Uid);
            Assert.IsNotNull(latest); Assert.AreEqual(2, latest.Version); Assert.AreEqual("来源事务前互斥后的版本", latest.Remark);
            Assert.AreEqual(1, (await new PostgreSqlSalesOrderHeadRepository(Context(oldReader)).GetByIdAsync(head.Uid))!.Version);
            Rollback(waiter); await prepared.DisposeAsync(); Rollback(oldReader);
            Assert.AreEqual(0L, await Locks(oldReader, holder)); Assert.AreEqual(0L, await Locks(oldReader, waiter));
        }
        finally
        {
            deadline.Cancel(); Rollback(holder); Rollback(waiter); Rollback(oldReader);
            if (held is not null) await held.DisposeAsync();
            try { if (pending is not null && await pending is { } late) await late.DisposeAsync(); }
            catch (OperationCanceledException) { }
            finally
            {
                await oldReader.Connection.ExecuteAsync("DELETE FROM public.sales_order_header WHERE uid=@Uid", new { head.Uid });
                Assert.AreEqual(0, await oldReader.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM public.sales_order_header WHERE uid=@Uid", new { head.Uid }));
            }
        }
    }

    /// <summary>内部来源能力直接执行，返回的Task保留原异步异常，不反射修改租约状态。</summary>
    private static Task<IAsyncDisposable?> Acquire(DbSession session, Guid[] sources, Guid[] gross, CancellationToken token = default)
        => (Task<IAsyncDisposable?>)AcquireMethod.Invoke(null, [session, sources, gross, NullLogger.Instance, token])!;
    /// <summary>严格故障库门禁创建独立物理请求连接。</summary>
    private static DbSession Session() => new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
    /// <summary>当前连接复用真实审计上下文，没有业务接线或新连接。</summary>
    private static RepositoryBaseContext Context(DbSession session)
    { var user = new User(); return new(session, NullLoggerFactory.Instance, new AuditStampService(user), user); }
    /// <summary>由独立观察者按真实backend PID计数会话锁。</summary>
    private static Task<long> Locks(DbSession observer, DbSession target) => Locks(observer, ((NpgsqlConnection)target.Connection).ProcessID);
    /// <summary>观察已关闭PID也返回零，不从已释放连接取得PID。</summary>
    private static Task<long> Locks(DbSession observer, int pid) => observer.Connection.ExecuteScalarAsync<long>(
        "SELECT COUNT(*) FROM pg_locks WHERE pid=@Pid AND locktype='advisory'", new { Pid = pid }, observer.Transaction);
    /// <summary>确认目标已真实执行生产try-lock，防止只检查尚未开始的Task；10秒令牌限定观察。</summary>
    private static async Task ObserveAttempt(DbSession observer, DbSession target, CancellationToken token)
    {
        var pid = ((NpgsqlConnection)target.Connection).ProcessID;
        while (!await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid=@Pid AND query LIKE '%pg_try_advisory_lock%')", new { Pid = pid }, cancellationToken: token)))
            await Task.Delay(25, token);
    }
    /// <summary>物理关闭是异步服务器事件，以真实目录最终锁零为准。</summary>
    private static async Task ObserveNoLocks(DbSession observer, int pid)
    { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8)); while (await Locks(observer, pid) != 0) await Task.Delay(25, deadline.Token); }
    /// <summary>本例拥有业务事务，不改变原隔离级别。</summary>
    private static void Begin(DbSession session, IsolationLevel isolation) => session.Transaction = session.Connection.BeginTransaction(isolation);
    /// <summary>只提交本例随机头，立即释放事务以便随后解锁。</summary>
    private static void Commit(DbSession session)
    { var tx = session.Transaction!; try { tx.Commit(); } finally { session.Transaction = null; tx.Dispose(); } }
    /// <summary>失败/结束先清理业务事务再释放会话锁。</summary>
    private static void Rollback(DbSession session)
    { if (session.Transaction is not { } tx) return; try { tx.Rollback(); } finally { session.Transaction = null; tx.Dispose(); } }
    /// <summary>独立测试审计身份，不借用真实业务用户。</summary>
    private sealed class User : ICurrentUser
    { public Guid? UserId => null; public string UserName => "__pgtest_source_preparation__"; public bool IsAuthenticated => false; }
}
