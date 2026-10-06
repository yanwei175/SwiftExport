using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.SalesOrders.Document;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>销售头行真实独立连接协议；仅提交随机本例实体，结束按登记UID逆序清理，不初始化或改变结构。</summary>
[TestClass, DoNotParallelize, TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlSalesOrderLineProtocolIntegrationTests
{
    /// <summary>原保存先锁头再读行：即使明细为空，第二请求仍在实际头查询等待，释放后读取新头版本与新增行。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ParentHeadLock_ProtectsEmptyAndExistingLineSet(bool existing)
    {
        using var holder = Session();
        using var waiter = Session();
        using var observer = Session();
        var first = new Repositories(holder);
        var second = new Repositories(waiter);
        var head = new SalesOrderHeader { OrderNo = "PGLP" + Guid.NewGuid().ToString("N"), CustomerId = Guid.NewGuid() };
        var registered = new List<Guid>();
        Task<(SalesOrderHeader Header, IReadOnlyList<SalesOrderLine> Lines)>? pending = null;
        try
        {
            Begin(holder);
            Assert.IsTrue((await first.Heads.AddAsync(head)).Success);
            if (existing)
            {
                var initial = Line(head, 1); registered.Add(initial.Uid);
                Assert.IsTrue((await first.Lines.AddAsync(initial)).Success);
            }
            Commit(holder);
            Begin(holder);
            var lockedHead = await first.Heads.GetByIdAsync(head.Uid, true);
            Assert.IsNotNull(lockedHead);
            var lockedLines = await first.Lines.GetLinesByOrderIdAsync(head.Uid, true);
            Assert.HasCount(existing ? 1 : 0, lockedLines);
            Begin(waiter);
            pending = ReadAfterParentLock(second, head.Uid);
            await ObserveProductionWait(observer, holder, waiter, "sales_order_header", "SELECT");

            // 先更新头再暂存旧行并恢复最终号，与原保存的父锁/行号阶段保持一致。
            lockedHead.Remark = "第一请求提交后版本";
            Assert.IsTrue((await first.Heads.UpdateAsync(lockedHead)).Success);
            Assert.AreEqual(existing ? 1 : 0, await first.Lines.StageLineNumbersAsync(head.Uid, existing ? 2 : 1));
            if (existing)
            {
                var line = lockedLines.Single(); line.LineNumber = 1; line.SubNote = "第一请求恢复最终行号";
                Assert.IsTrue((await first.Lines.UpdateAsync(line)).Success);
            }
            var added = Line(head, existing ? 2 : 1); registered.Add(added.Uid);
            Assert.IsTrue((await first.Lines.AddAsync(added)).Success);
            Commit(holder);

            var after = await pending;
            Assert.AreEqual(2, after.Header.Version);
            Assert.AreEqual("第一请求提交后版本", after.Header.Remark);
            Assert.HasCount(existing ? 2 : 1, after.Lines);
            Assert.AreEqual(added.Uid, after.Lines.Last().Uid);
            CollectionAssert.AreEqual(existing ? new[] { 1, 2 } : new[] { 1 }, after.Lines.Select(x => x.LineNumber).ToArray());
            if (existing) Assert.AreEqual(2, after.Lines.First().Version);
            // 仓储不替服务校验ExpectedVersion；此处证明后续校验读取的是提交后的2而非旧1。
            Assert.AreNotEqual(1, after.Header.Version);
            Rollback(waiter);
            await AssertReleased(observer, holder, waiter);
        }
        finally
        {
            Rollback(holder);
            try { if (pending is not null) await pending; }
            finally { Rollback(waiter); await Cleanup(observer, head.Uid, registered); }
        }
    }

    /// <summary>现存行的生产FOR UPDATE阻止第二连接旧版本删除；第一连接更新提交后，删除返回源并发失败而非40001。</summary>
    [TestMethod]
    public async Task ExistingLineLock_OldVersionDeleteWaitsAndConflicts()
    {
        using var holder = Session(); using var waiter = Session(); using var observer = Session();
        var first = new Repositories(holder); var second = new Repositories(waiter);
        var head = new SalesOrderHeader { OrderNo = "PGLP" + Guid.NewGuid().ToString("N"), CustomerId = Guid.NewGuid() };
        SalesOrderLine? seeded = null;
        Task<IReadOnlyList<RepositoryWriteResult>>? pending = null;
        try
        {
            Begin(holder); Assert.IsTrue((await first.Heads.AddAsync(head)).Success);
            seeded = Line(head, 1); Assert.IsTrue((await first.Lines.AddAsync(seeded)).Success); Commit(holder);
            Begin(holder);
            var locked = (await first.Lines.GetLinesByOrderIdAsync(head.Uid, true)).Single();
            Begin(waiter);
            pending = second.Lines.DeleteLinesAsync([new SalesOrderDeletedLineDto { Uid = seeded.Uid, Version = 1 }]);
            await ObserveProductionWait(observer, holder, waiter, "sales_order_line", "UPDATE");
            locked.SubNote = "独立请求提交的行";
            Assert.IsTrue((await first.Lines.UpdateAsync(locked)).Success); Commit(holder);
            var result = (await pending).Single();
            Assert.IsFalse(result.Success); Assert.AreEqual(seeded.Uid, result.Uid);
            Assert.AreEqual(1, result.OldVersion); Assert.IsNull(result.NewVersion);
            Assert.AreEqual("CONCURRENCY_CONFLICT", result.ErrorCode);
            Assert.AreEqual("删除销售订单行失败，数据已被其他用户修改。", result.Message);
            var current = (await second.Lines.GetLinesByOrderIdAsync(head.Uid)).Single();
            Assert.AreEqual(2, current.Version); Assert.IsFalse(current.IsDeleted);
            Assert.AreEqual("独立请求提交的行", current.SubNote);
            Rollback(waiter); await AssertReleased(observer, holder, waiter);
        }
        finally
        {
            Rollback(holder);
            try { if (pending is not null) await pending; }
            finally { Rollback(waiter); await Cleanup(observer, head.Uid, seeded is null ? [] : [seeded.Uid]); }
        }
    }

    /// <summary>按原父头→有效行顺序直接调用两份生产仓储；等待期间不插入替身SQL或事务内自写。</summary>
    private static async Task<(SalesOrderHeader, IReadOnlyList<SalesOrderLine>)> ReadAfterParentLock(Repositories repositories, Guid uid)
    {
        var head = await repositories.Heads.GetByIdAsync(uid, true)
            ?? throw new InvalidOperationException("随机销售头应存在。");
        return (head, await repositories.Lines.GetLinesByOrderIdAsync(uid, true));
    }

    /// <summary>第三物理连接观察真实blocking PID和未授予锁，限定10秒；轮询仅等待已发生数据库互斥，不以固定睡眠猜测。</summary>
    private static async Task ObserveProductionWait(DbSession observer, DbSession holder, DbSession waiter, string table, string verb)
    {
        var heldPid = ((NpgsqlConnection)holder.Connection).ProcessID;
        var waitingPid = ((NpgsqlConnection)waiter.Connection).ProcessID;
        Assert.AreNotEqual(heldPid, waitingPid);
        Assert.AreNotEqual(waitingPid, ((NpgsqlConnection)observer.Connection).ProcessID);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT @Holder=ANY(pg_blocking_pids(@Waiter))", new { Holder = heldPid, Waiter = waitingPid }, cancellationToken: deadline.Token)))
            await Task.Delay(25, deadline.Token);
        var sql = await observer.Connection.ExecuteScalarAsync<string>("SELECT query FROM pg_stat_activity WHERE pid=@Pid", new { Pid = waitingPid });
        Assert.IsNotNull(sql); StringAssert.Contains(sql, verb);
        // 实例查询文本只保留前1024字节，头的长投影尾部表名及FOR UPDATE可能截断；以真实relation OID核验表归属。
        Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM pg_locks WHERE pid=@Pid AND locktype='relation' AND relation=to_regclass(@Table))",
            new { Pid = waitingPid, Table = "public." + table }));
        Assert.IsFalse(sql.Contains("pg_sleep", StringComparison.Ordinal));
        Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM pg_locks WHERE pid=@Pid AND NOT granted)", new { Pid = waitingPid }));
    }

    /// <summary>请求事务结束后连接仍可用，行/事务/咨询锁不得残留。</summary>
    private static async Task AssertReleased(DbSession observer, params DbSession[] requests)
    {
        foreach (var session in requests)
        {
            Assert.IsNull(session.Transaction);
            Assert.AreEqual(1, await session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
            Assert.AreEqual(0L, await observer.Connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM pg_locks WHERE pid=@Pid AND locktype IN ('relation','tuple','transactionid','advisory')",
                new { Pid = ((NpgsqlConnection)session.Connection).ProcessID }));
        }
    }

    /// <summary>仅清理登记销售行且再次限定本例父头，最后删除本例头；失败也finally回滚，不改其他行或结构。</summary>
    private static async Task Cleanup(DbSession observer, Guid headUid, IEnumerable<Guid> lines)
    {
        using var tx = observer.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            foreach (var uid in lines)
                await observer.Connection.ExecuteAsync("DELETE FROM public.sales_order_line WHERE uid=@Uid AND sales_order_id=@Head", new { Uid = uid, Head = headUid }, tx);
            await observer.Connection.ExecuteAsync("DELETE FROM public.sales_order_header WHERE uid=@Uid", new { Uid = headUid }, tx);
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>(
                "SELECT (SELECT COUNT(*) FROM public.sales_order_header WHERE uid=@Uid)+(SELECT COUNT(*) FROM public.sales_order_line WHERE sales_order_id=@Uid)", new { Uid = headUid }, tx));
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>合法非标行保持指定随机UID和正行号/数量，不创建主数据或业务来源。</summary>
    private static SalesOrderLine Line(SalesOrderHeader head, int number) => new()
    { Uid = Guid.NewGuid(), SalesOrderId = head.Uid, OrderNo = head.OrderNo, LineNumber = number, IsNonStandard = true, Sku = "PGLP" + Guid.NewGuid().ToString("N"), Qty = 10m, UnitSnapshot = "PCS" };
    /// <summary>严格使用故障测试门禁创建一份独立连接，禁止切库或初始化。</summary>
    private static DbSession Session() => new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
    /// <summary>本例请求拥有ReadCommitted事务。</summary>
    private static void Begin(DbSession session) => session.Transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
    /// <summary>仅提交已登记本例实体并立即释放请求事务。</summary>
    private static void Commit(DbSession session)
    { var tx = session.Transaction!; try { tx.Commit(); } finally { session.Transaction = null; tx.Dispose(); } }
    /// <summary>先释放请求事务再清理实体，避免清理连接等待自己；已结束事务不重复回滚。</summary>
    private static void Rollback(DbSession session)
    { if (session.Transaction is not { } tx) return; try { tx.Rollback(); } finally { session.Transaction = null; tx.Dispose(); } }
    /// <summary>两生产仓储复用同一请求Session与审计上下文。</summary>
    private sealed class Repositories
    {
        public PostgreSqlSalesOrderHeadRepository Heads { get; }
        public PostgreSqlSalesOrderLineRepository Lines { get; }
        /// <summary>创建仓储但不替调用者创建事务或改变DI。</summary>
        public Repositories(DbSession session)
        { var user = new User(); var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user); Heads = new(context); Lines = new(context); }
    }
    /// <summary>独立夹具审计身份，不读取真实用户。</summary>
    private sealed class User : ICurrentUser
    { public Guid? UserId => null; public string UserName => "__pgtest_line_protocol__"; public bool IsAuthenticated => false; }
}
