using System.Data;
using System.Diagnostics;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Commands;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>真实会话验证命令回执与事务锁；所有回执写入回滚，提交场景只持锁而不写表。</summary>
[TestClass]
public sealed class PostgreSqlCommandReceiptMigrationIntegrationTests
{
    /// <summary>同事务读回完整原始回执及可空目标，UTC 精度和结果 JSON 均保留，最终回滚。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Receipt_RoundTripsInsideRollbackTransaction(bool hasTarget)
    {
        using var fixture = new RealFixture();
        var receipt = TestReceipt();
        receipt.RequestTargetUid = hasTarget ? Guid.NewGuid() : null;
        Assert.IsNull(await fixture.Repository.FindAsync(receipt.CommandId, default));
        Assert.AreEqual(0, await fixture.Repository.LockAsync(receipt.CommandId, default));
        await fixture.Repository.InsertAsync(receipt, default);
        var stored = await fixture.Repository.FindAsync(receipt.CommandId, default);
        Assert.IsNotNull(stored);
        Assert.AreEqual(receipt.Uid, stored.Uid);
        Assert.AreEqual(receipt.Version, stored.Version);
        Assert.AreEqual(receipt.IsDeleted, stored.IsDeleted);
        Assert.AreEqual(receipt.CommandId, stored.CommandId);
        Assert.AreEqual(receipt.ActorUid, stored.ActorUid);
        Assert.AreEqual(receipt.CommandType, stored.CommandType);
        Assert.AreEqual(receipt.RequestTargetUid, stored.RequestTargetUid);
        Assert.AreEqual(receipt.FingerprintVersion, stored.FingerprintVersion);
        Assert.AreEqual(receipt.RequestFingerprint, stored.RequestFingerprint);
        Assert.AreEqual(receipt.ResultVersion, stored.ResultVersion);
        Assert.AreEqual(receipt.ResultJson, stored.ResultJson);
        Assert.AreEqual(receipt.RecordedAtUtc, stored.RecordedAtUtc);
        Assert.AreEqual(DateTimeKind.Utc, stored.RecordedAtUtc.Kind);
        Assert.IsNotNull(stored.CreatedDate);
        Assert.IsNotNull(stored.UpdatedDate);
        Assert.AreEqual(receipt.CreatedBy, stored.CreatedBy);
        Assert.AreEqual(receipt.UpdatedBy, stored.UpdatedBy);
    }

    /// <summary>完整 CommandId 唯一索引拒绝第二条回执，不使用 UPSERT 或提交后删除清理。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlIntegration")]
    public async Task Receipt_DuplicateCommandIdPreservesUniqueConflict()
    {
        using var fixture = new RealFixture();
        var first = TestReceipt();
        await fixture.Repository.InsertAsync(first, default);
        var duplicate = TestReceipt();
        duplicate.CommandId = first.CommandId;
        var error = await Assert.ThrowsAsync<PostgresException>(() => fixture.Repository.InsertAsync(duplicate, default));
        Assert.AreEqual("23505", error.SqlState);
        // 预期数据库错误使当前事务失败，直接由夹具 finally 回滚，不继续查询或提交。
    }

    /// <summary>只对本事务专用回执尝试更新或删除，验证不可变触发器 P3100 后整体回滚。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Receipt_AppendOnlyTriggerRejectsMutation(bool delete)
    {
        using var fixture = new RealFixture();
        var receipt = TestReceipt();
        await fixture.Repository.InsertAsync(receipt, default);
        var sql = delete
            ? "DELETE FROM public.command_receipt WHERE uid = @Uid;"
            : "UPDATE public.command_receipt SET result_json = @ResultJson WHERE uid = @Uid;";
        var error = await Assert.ThrowsAsync<PostgresException>(() => fixture.Session.Connection.ExecuteAsync(
            new CommandDefinition(sql, new { receipt.Uid, ResultJson = "{\"Changed\":true}" }, fixture.Session.Transaction)));
        Assert.AreEqual("P3100", error.SqlState);
    }

    /// <summary>同命令跨连接串行、不同命令可同时持锁；只有未写表事务提交或回滚后等待者才能取锁。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Lock_ReleaseByRollbackOrCommitAllowsWaitingOwner(bool commit)
    {
        using var owner = new RealFixture();
        using var waiter = new RealFixture();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var commandId = Guid.NewGuid();
        Assert.AreEqual(0, await owner.Repository.LockAsync(commandId, default));
        Assert.AreEqual(0, await waiter.Repository.LockAsync(Guid.NewGuid(), default));
        var waiting = waiter.Repository.LockAsync(commandId, cancellation.Token);
        try
        {
            await Task.Delay(150);
            Assert.IsFalse(waiting.IsCompleted, "同一 CommandId 在拥有者释放事务前不能取锁。");
            if (commit) owner.Session.Transaction!.Commit();
            else owner.Session.Transaction!.Rollback();
            // 本测试主动结束的事务必须立即释放并清空，夹具只回滚仍由它持有的活动事务。
            owner.Session.Transaction.Dispose();
            owner.Session.Transaction = null;
            Assert.AreEqual(0, await waiting);
        }
        finally
        {
            // 失败时先结束本测试启动的等待，防止仍在运行的命令访问已释放连接。
            if (!waiting.IsCompleted)
            {
                cancellation.Cancel();
                try { await waiting; }
                catch (OperationCanceledException) { }
            }
        }
    }

    /// <summary>持续竞争耗尽十秒预算返回负一，不污染外层 lock_timeout，也不使事务失效。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlIntegration")]
    public async Task Lock_CompetitionTimesOutWithoutChangingOuterTimeout()
    {
        using var owner = new RealFixture();
        using var waiter = new RealFixture();
        var commandId = Guid.NewGuid();
        Assert.AreEqual(0, await owner.Repository.LockAsync(commandId, default));
        var originalTimeout = await waiter.Session.Connection.ExecuteScalarAsync<string>(
            new CommandDefinition("SHOW lock_timeout;", transaction: waiter.Session.Transaction));
        var elapsed = Stopwatch.StartNew();
        Assert.AreEqual(-1, await waiter.Repository.LockAsync(commandId, default));
        Assert.IsTrue(elapsed.Elapsed >= TimeSpan.FromSeconds(9), "竞争预算不能立即退化为零等待。");
        var afterTimeout = await waiter.Session.Connection.ExecuteScalarAsync<string>(
            new CommandDefinition("SHOW lock_timeout;", transaction: waiter.Session.Transaction));
        Assert.AreEqual(originalTimeout, afterTimeout);
        Assert.AreEqual(1, await waiter.Session.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT 1;", transaction: waiter.Session.Transaction)));
    }

    /// <summary>预取消及等待中取消均传播取消异常，不返回繁忙码；不追加任何回执。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlIntegration")]
    public async Task Lock_CancellationIsNotBusy()
    {
        using var owner = new RealFixture();
        using var waiter = new RealFixture();
        var commandId = Guid.NewGuid();
        await Assert.ThrowsAsync<OperationCanceledException>(() => waiter.Repository.LockAsync(commandId, new CancellationToken(true)));
        Assert.AreEqual(0, await owner.Repository.LockAsync(commandId, default));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAsync<OperationCanceledException>(() => waiter.Repository.LockAsync(commandId, cancellation.Token));
    }

    /// <summary>固定快照隔离级别在取锁前明确拒绝，不能误称已支持采购、售后或箱单的完整重放。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlIntegration")]
    [DataRow(IsolationLevel.RepeatableRead)]
    [DataRow(IsolationLevel.Serializable)]
    public async Task Lock_FixedSnapshotIsolationIsExplicitlyRejected(IsolationLevel isolation)
    {
        using var fixture = new RealFixture(isolation);
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Repository.LockAsync(Guid.NewGuid(), default));
        Assert.AreEqual(isolation, fixture.Session.Transaction!.IsolationLevel);
        Assert.AreEqual(1, await fixture.Session.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT 1;", transaction: fixture.Session.Transaction)));
    }

    /// <summary>无调用方事务时拒绝取锁和追加，而普通缺失回执读取仍可使用自动提交查询。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlIntegration")]
    public async Task Receipt_NoTransactionRejectsWritesButAllowsRead()
    {
        using var fixture = new RealFixture();
        fixture.Session.Transaction!.Rollback();
        fixture.Session.Transaction.Dispose();
        fixture.Session.Transaction = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Repository.LockAsync(Guid.NewGuid(), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Repository.InsertAsync(TestReceipt(), default));
        Assert.IsNull(await fixture.Repository.FindAsync(Guid.NewGuid(), default));
    }

    /// <summary>构造独立随机身份及合法测试指纹，不引用或修改任何真实业务回执。</summary>
    private static CommandReceipt TestReceipt() => new()
    {
        Uid = Guid.NewGuid(), CommandId = Guid.NewGuid(), ActorUid = Guid.NewGuid(),
        CommandType = "__pgtest_" + Guid.NewGuid().ToString("N"),
        RequestFingerprint = new string('A', 64), FingerprintVersion = 1, ResultVersion = 2,
        ResultJson = "{\"Value\":1}", RecordedAtUtc = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc).AddTicks(1234560)
    };

    /// <summary>提供真实连接与外层事务；释放时回滚未结束事务，所有回执测试都不提交。</summary>
    private sealed class RealFixture : IDisposable
    {
        public DbSession Session { get; }
        public PostgreSqlCommandReceiptRepository Repository { get; }

        /// <summary>仅使用主控已校验的显式测试连接，初始化失败时释放连接。</summary>
        public RealFixture(IsolationLevel isolation = IsolationLevel.ReadCommitted)
        {
            Session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetConnectionString()));
            try
            {
                Session.Transaction = Session.Connection.BeginTransaction(isolation);
                var user = new TestCurrentUser();
                Repository = new(new RepositoryBaseContext(Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
            }
            catch { Session.Dispose(); throw; }
        }

        /// <summary>finally 回滚仅本夹具的未提交事务，随后释放会话，不执行 DELETE 或禁用触发器。</summary>
        public void Dispose()
        {
            try { if (Session.Transaction?.Connection is not null) Session.Transaction.Rollback(); }
            finally { Session.Dispose(); }
        }
    }

    /// <summary>提供专用审计名称，不模拟已有业务账号。</summary>
    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_command_receipt__";
        public bool IsAuthenticated => false;
    }
}
