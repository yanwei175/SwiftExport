using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Commands;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Commands;

/// <summary>验证公共执行顺序、持久化结果重放和事务失败语义；真实 SQL 锁由数据库专项负责。</summary>
[TestClass]
public sealed class BusinessCommandExecutorTests
{
    /// <summary>同一身份和输入返回首次结果，不再次调用业务写入委托。</summary>
    [TestMethod]
    public async Task SuccessAndReplay_ReturnOriginalResult()
    {
        var f = new Fixture();
        var id = Guid.NewGuid();
        var writes = 0;
        Task<Result> Write(CancellationToken _) { writes++; return Task.FromResult(new Result(Guid.NewGuid(), 2)); }
        var first = await f.Executor.ExecuteAsync(id, "Inventory.Inbound.Draft.Create", null, new { Quantity = 2m }, Write, default);
        var replay = await f.Executor.ExecuteAsync(id, "Inventory.Inbound.Draft.Create", null, new { Quantity = 2m }, Write, default);
        Assert.AreEqual(first.Value, replay.Value);
        Assert.IsFalse(first.IsReplay);
        Assert.IsTrue(replay.IsReplay);
        Assert.AreEqual(1, writes);
        Assert.AreEqual(1, f.Receipts.Inserts);
        Assert.AreEqual(2, f.Connection.Commits);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>输入对象属性顺序和等值小数格式不改变业务意图。</summary>
    [TestMethod]
    public async Task EquivalentPropertyOrderAndDecimalScale_Replay()
    {
        var f = new Fixture();
        var id = Guid.NewGuid();
        var writes = 0;
        Task<Result> Write(CancellationToken _) { writes++; return Task.FromResult(new Result(Guid.NewGuid(), 1)); }
        var first = await f.Executor.ExecuteAsync(id, "Inventory.Inbound.Draft.Create", null,
            new { Quantity = 1.00m, Code = "A" }, Write, default);
        var replay = await f.Executor.ExecuteAsync(id, "Inventory.Inbound.Draft.Create", null,
            new { Code = "A", Quantity = 1.0m }, Write, default);
        Assert.AreEqual(first.Value, replay.Value);
        Assert.IsFalse(first.IsReplay);
        Assert.IsTrue(replay.IsReplay);
        Assert.AreEqual(1, writes);
    }

    /// <summary>同一 CommandId 不能换操作者、操作、目标或业务输入。</summary>
    [TestMethod]
    public async Task SameId_DifferentIdentityOrInput_Conflicts()
    {
        foreach (var difference in new[] { "actor", "operation", "target", "input" })
        {
            var f = new Fixture();
            var id = Guid.NewGuid();
            var target = Guid.NewGuid();
            var writes = 0;
            Task<Result> Write(CancellationToken _) { writes++; return Task.FromResult(new Result(Guid.NewGuid(), 1)); }
            await f.Executor.ExecuteAsync(id, "Inventory.Inbound.Draft.Update", target, new { Version = 1 }, Write, default);
            if (difference == "actor") f.User.UserIdValue = Guid.NewGuid();
            var ex = await Assert.ThrowsAsync<BusinessCommandException>(() => f.Executor.ExecuteAsync(id,
                difference == "operation" ? "Inventory.Outbound.Draft.Update" : "Inventory.Inbound.Draft.Update",
                difference == "target" ? Guid.NewGuid() : target,
                new { Version = difference == "input" ? 2 : 1 }, Write, default));
            Assert.AreEqual(ServiceErrorCodes.CommandConflict, ex.Code);
            Assert.AreEqual(1, writes);
        }
    }

    /// <summary>锁繁忙和已有外层事务不得执行委托；取消在开启事务前终止。</summary>
    [TestMethod]
    public async Task BusyOuterTransactionAndCancellation_DoNotWrite()
    {
        var f = new Fixture();
        var writes = 0;
        Task<Result> Write(CancellationToken _) { writes++; return Task.FromResult(new Result(Guid.NewGuid(), 1)); }
        f.Receipts.LockResult = -1;
        var busy = await Assert.ThrowsAsync<BusinessCommandException>(() => f.Executor.ExecuteAsync(
            Guid.NewGuid(), "Inventory.Inbound.Draft.Create", null, new { Value = 1 }, Write, default));
        Assert.AreEqual(ServiceErrorCodes.CommandBusy, busy.Code);
        Assert.AreEqual(1, f.Connection.Rollbacks);
        f.Session.Transaction = f.Connection.BeginTransaction();
        var nested = await Assert.ThrowsAsync<BusinessCommandException>(() => f.Executor.ExecuteAsync(
            Guid.NewGuid(), "Inventory.Inbound.Draft.Create", null, new { Value = 1 }, Write, default));
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, nested.Code);
        Assert.IsNotNull(f.Session.Transaction);
        f.Session.Transaction = null;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Executor.ExecuteAsync(
            Guid.NewGuid(), "Inventory.Inbound.Draft.Create", null, new { Value = 1 }, Write, cancellation.Token));
        Assert.AreEqual(0, writes);
    }

    /// <summary>回执写入失败回滚；提交结果不明时只返回结果未知，客户端保留原命令。</summary>
    [TestMethod]
    public async Task ReceiptFailureAndCommitUncertainty_AreNotSuccess()
    {
        var f = new Fixture();
        f.Receipts.FailInsert = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Executor.ExecuteAsync(
            Guid.NewGuid(), "Inventory.Inbound.Draft.Create", null, new { Value = 1 },
            _ => Task.FromResult(new Result(Guid.NewGuid(), 1)), default));
        Assert.AreEqual(1, f.Connection.Rollbacks);
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.IsNull(f.Session.Transaction);

        var unknown = new Fixture();
        unknown.Connection.FailCommit = true;
        var ex = await Assert.ThrowsAsync<BusinessCommandException>(() => unknown.Executor.ExecuteAsync(
            Guid.NewGuid(), "Inventory.Inbound.Draft.Create", null, new { Value = 1 },
            _ => Task.FromResult(new Result(Guid.NewGuid(), 1)), default));
        Assert.AreEqual(ServiceErrorCodes.OutcomeUnknown, ex.Code);
        Assert.IsNull(unknown.Session.Transaction);
    }

    /// <summary>未知回执格式不能被当作未执行命令重新运行。</summary>
    [TestMethod]
    public async Task UnknownResultVersion_StopsReplay()
    {
        var f = new Fixture();
        var id = Guid.NewGuid();
        await f.Executor.ExecuteAsync(id, "Inventory.Inbound.Draft.Create", null, new { Value = 1 },
            _ => Task.FromResult(new Result(Guid.NewGuid(), 1)), default);
        f.Receipts.Rows[id].ResultVersion++;
        var ex = await Assert.ThrowsAsync<BusinessCommandException>(() => f.Executor.ExecuteAsync<Result>(id,
            "Inventory.Inbound.Draft.Create", null, new { Value = 1 },
            _ => throw new AssertFailedException("未知结果版本不得再次执行业务。"), default));
        Assert.AreEqual(ServiceErrorCodes.OutcomeUnknown, ex.Code);
    }

    private sealed record Result(Guid Uid, int Version);

    /// <summary>共享会话与可观察仓储替身，只验证 Application 编排。</summary>
    private sealed class Fixture
    {
        public readonly ConnectionStub Connection = new();
        public readonly ReceiptStub Receipts;
        public readonly CurrentUserStub User = new();
        public readonly DbSession Session;
        public readonly BusinessCommandExecutor Executor;

        public Fixture()
        {
            Session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Session, Connection);
            Receipts = new ReceiptStub(Session);
            Executor = new BusinessCommandExecutor(new ServiceBaseContext(Session, NullLoggerFactory.Instance), Receipts, User);
        }
    }

    /// <summary>模拟已成功保存的回执及失败注入，不模拟 SQL Server 的锁和唯一索引。</summary>
    private sealed class ReceiptStub(DbSession session) : ICommandReceiptRepository
    {
        public readonly Dictionary<Guid, CommandReceipt> Rows = [];
        public int LockResult, Inserts;
        public bool FailInsert;
        public Task<int> LockAsync(Guid commandId, CancellationToken token)
        {
            Assert.IsNotNull(session.Transaction);
            return Task.FromResult(LockResult);
        }
        public Task<CommandReceipt?> FindAsync(Guid commandId, CancellationToken token) => Task.FromResult(Rows.GetValueOrDefault(commandId));
        public Task InsertAsync(CommandReceipt receipt, CancellationToken token)
        {
            Assert.IsNotNull(session.Transaction);
            if (FailInsert) throw new InvalidOperationException("模拟回执写入失败");
            Rows.Add(receipt.CommandId, receipt);
            Inserts++;
            return Task.CompletedTask;
        }
    }

    /// <summary>提供可切换的已认证用户，验证回执身份比较。</summary>
    private sealed class CurrentUserStub : ICurrentUser
    {
        public Guid? UserIdValue = Guid.NewGuid();
        public string UserName => "测试";
        public Guid? UserId => UserIdValue;
        public bool IsAuthenticated => UserIdValue.HasValue;
    }

    /// <summary>记录事务提交与回滚，并允许模拟提交结果未知。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public int Commits, Rollbacks;
        public bool FailCommit;
        [AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "test";
        public ConnectionState State => ConnectionState.Open;
        public IDbTransaction BeginTransaction() => new TransactionStub(this);
        public IDbTransaction BeginTransaction(IsolationLevel il) => BeginTransaction();
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public void Close() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() { }
        public void Dispose() { }
        private sealed class TransactionStub(ConnectionStub connection) : IDbTransaction
        {
            public IDbConnection Connection => connection;
            public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            public void Commit() { if (connection.FailCommit) throw new IOException("模拟提交连接中断"); connection.Commits++; }
            public void Rollback() { connection.Rollbacks++; }
            public void Dispose() { }
        }
    }
}
