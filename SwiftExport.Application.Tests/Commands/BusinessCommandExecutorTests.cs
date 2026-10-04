using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
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

    /// <summary>显式可串行化只作用于当前单据命令，未指定时继续调用连接默认事务入口。</summary>
    [TestMethod]
    public async Task ExplicitIsolation_DoesNotChangeDefaultCommands()
    {
        var f = new Fixture();
        await f.Executor.ExecuteAsync(Guid.NewGuid(), "Shipping.PackingList.Post", Guid.NewGuid(), new { Version = 1 },
            _ => Task.FromResult(new Result(Guid.NewGuid(), 1)), default, IsolationLevel.Serializable);
        Assert.AreEqual(IsolationLevel.Serializable, f.Connection.LastRequestedIsolation);
        await f.Executor.ExecuteAsync(Guid.NewGuid(), "Inventory.Inbound.Post", Guid.NewGuid(), new { Version = 1 },
            _ => Task.FromResult(new Result(Guid.NewGuid(), 1)), default);
        Assert.IsNull(f.Connection.LastRequestedIsolation);
    }

    /// <summary>准备租约先于事务，所有已取得租约的业务出口都在事务释放后清理。</summary>
    [TestMethod]
    [DataRow("success")]
    [DataRow("replay")]
    [DataRow("conflict")]
    [DataRow("failure")]
    [DataRow("cancellation")]
    public async Task PreparedCommand_ReleasesAfterTransactionOnEveryExit(string scenario)
    {
        var f = new Fixture(prepared: true);
        var id = Guid.NewGuid();
        var writes = 0;
        using var cancellation = new CancellationTokenSource();
        Task<Result> Write(CancellationToken _)
        {
            f.Connection.Events.Add("Action");
            writes++;
            if (scenario == "failure") throw new InvalidOperationException("模拟业务失败");
            if (scenario == "cancellation") cancellation.Cancel();
            return Task.FromResult(new Result(Guid.NewGuid(), 1));
        }
        if (scenario is "replay" or "conflict")
        {
            await f.Executor.ExecuteAsync(id, "Inventory.Inbound.Draft.Create", null, new { Value = 1 }, Write, default);
            f.Connection.Events.Clear();
        }
        Task<CommandExecutionResult<Result>> Execute() => f.Executor.ExecuteAsync(id,
            "Inventory.Inbound.Draft.Create", null, new { Value = scenario == "conflict" ? 2 : 1 }, Write, cancellation.Token);
        if (scenario == "conflict")
            Assert.AreEqual(ServiceErrorCodes.CommandConflict, (await Assert.ThrowsAsync<BusinessCommandException>(Execute)).Code);
        else if (scenario == "failure") await Assert.ThrowsAsync<InvalidOperationException>(Execute);
        else if (scenario == "cancellation") await Assert.ThrowsAsync<OperationCanceledException>(Execute);
        else Assert.AreEqual(scenario == "replay", (await Execute()).IsReplay);

        var events = f.Connection.Events;
        Assert.AreEqual("Acquire", events[0]);
        Assert.AreEqual("Begin", events[1]);
        Assert.AreEqual("TransactionDispose", events[^2]);
        Assert.AreEqual("LeaseDispose", events[^1]);
        Assert.AreEqual(1, events.Count(x => x == "LeaseDispose"));
        Assert.AreEqual(1, writes);
        Assert.AreEqual(scenario is "success" or "replay" ? 1 : 0, events.Count(x => x == "Commit"));
        Assert.AreEqual(scenario is "conflict" or "failure" or "cancellation" ? 1 : 0,
            events.Count(x => x == "Rollback"));
        Assert.IsNull(f.Session.Transaction);
        Assert.AreEqual(id, ((PreparedReceiptStub)f.Receipts).LastCommandId);
    }

    /// <summary>准备阶段繁忙不创建事务、不查询回执，也不调用业务委托。</summary>
    [TestMethod]
    public async Task PreparedCommand_BusyDoesNotBeginTransaction()
    {
        var f = new Fixture(prepared: true);
        ((PreparedReceiptStub)f.Receipts).Busy = true;
        var ex = await Assert.ThrowsAsync<BusinessCommandException>(() => f.Executor.ExecuteAsync<Result>(
            Guid.NewGuid(), "Inventory.Inbound.Draft.Create", null, new { Value = 1 },
            _ => throw new AssertFailedException("准备繁忙不得执行业务。"), default));
        Assert.AreEqual(ServiceErrorCodes.CommandBusy, ex.Code);
        CollectionAssert.AreEqual(new[] { "Acquire" }, f.Connection.Events);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>取得租约后的取消及开启事务失败仍释放租约，不伪造事务回滚。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PreparedCommand_FailureBeforeTransactionReleasesLease(bool cancel)
    {
        var f = new Fixture(prepared: true);
        using var cancellation = new CancellationTokenSource();
        if (cancel) ((PreparedReceiptStub)f.Receipts).AfterAcquire = cancellation.Cancel;
        else f.Connection.FailBegin = true;
        Task<CommandExecutionResult<Result>> Execute() => f.Executor.ExecuteAsync<Result>(
            Guid.NewGuid(), "Inventory.Inbound.Draft.Create", null, new { Value = 1 },
            _ => throw new AssertFailedException("事务前失败不得执行业务。"), cancellation.Token);
        if (cancel) await Assert.ThrowsAsync<OperationCanceledException>(Execute);
        else await Assert.ThrowsAsync<InvalidOperationException>(Execute);
        CollectionAssert.AreEqual(cancel ? new[] { "Acquire", "LeaseDispose" }
            : new[] { "Acquire", "Begin", "LeaseDispose" }, f.Connection.Events);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>无准备能力的旧仓储继续在事务内取锁，不增加准备或租约步骤。</summary>
    [TestMethod]
    public async Task UnpreparedCommand_KeepsOriginalOrder()
    {
        var f = new Fixture();
        await f.Executor.ExecuteAsync(Guid.NewGuid(), "Inventory.Inbound.Draft.Create", null, new { Value = 1 },
            _ => { f.Connection.Events.Add("Action"); return Task.FromResult(new Result(Guid.NewGuid(), 1)); }, default);
        CollectionAssert.AreEqual(new[] { "Begin", "Lock", "Find", "Action", "Insert", "Commit", "TransactionDispose" },
            f.Connection.Events);
    }

    /// <summary>首次提交故障保持结果未知，单次请求不自动重试业务或提交。</summary>
    [TestMethod]
    public async Task FirstCommitFailure_ReturnsOutcomeUnknownWithoutRetry()
    {
        var f = new Fixture(prepared: true);
        f.Connection.FailCommit = true;
        var writes = 0;
        var ex = await Assert.ThrowsAsync<BusinessCommandException>(() => f.Executor.ExecuteAsync(
            Guid.NewGuid(), "Inventory.Inbound.Draft.Create", null, new { Value = 1 },
            _ => { writes++; return Task.FromResult(new Result(Guid.NewGuid(), 1)); }, default));
        Assert.AreEqual(ServiceErrorCodes.OutcomeUnknown, ex.Code);
        Assert.AreEqual(1, writes);
        Assert.AreEqual(1, f.Receipts.Inserts);
        Assert.AreEqual(1, f.Connection.Events.Count(x => x == "Commit"));
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.AreEqual(1, f.Connection.Rollbacks);
        AssertReleasedResources(f);
    }

    /// <summary>记录现有契约：重放提交故障原样传播，不包装成首次提交的结果未知，不再次执行业务。</summary>
    [TestMethod]
    public async Task ReplayCommitFailure_PreservesCurrentExceptionWithoutRetry()
    {
        var f = new Fixture(prepared: true);
        var id = Guid.NewGuid();
        var writes = 0;
        Task<Result> Write(CancellationToken _) { writes++; return Task.FromResult(new Result(Guid.NewGuid(), 1)); }
        await f.Executor.ExecuteAsync(id, "Inventory.Inbound.Draft.Create", null, new { Value = 1 }, Write, default);
        f.Connection.Events.Clear();
        var original = new IOException("模拟重放提交失败");
        f.Connection.CommitFailure = original;
        var ex = await Assert.ThrowsAsync<IOException>(() => f.Executor.ExecuteAsync(id,
            "Inventory.Inbound.Draft.Create", null, new { Value = 1 }, Write, default));
        Assert.AreSame(original, ex);
        Assert.AreEqual(1, writes);
        Assert.AreEqual(1, f.Receipts.Inserts);
        Assert.AreEqual(1, f.Connection.Events.Count(x => x == "Commit"));
        AssertReleasedResources(f);
    }

    /// <summary>回滚、释放和日志故障不得替换业务原异常；事务释放失败时必须关闭连接。</summary>
    [TestMethod]
    [TestCategory("CleanupFailureRegression")]
    [DataRow(true, false, null)]
    [DataRow(false, true, null)]
    [DataRow(true, true, null)]
    [DataRow(true, true, "create")]
    [DataRow(true, true, "write")]
    public async Task ActionFailure_CleanupMustPreserveOriginalException(bool failRollback, bool failDispose, string? loggerFailure)
    {
        var f = new Fixture(prepared: true, loggerFailure: loggerFailure);
        f.Connection.FailRollback = failRollback;
        f.Connection.FailTransactionDispose = failDispose;
        var original = new InvalidOperationException("模拟原始业务异常");
        var writes = 0;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Executor.ExecuteAsync<Result>(
            Guid.NewGuid(), "Inventory.Inbound.Draft.Create", null, new { Value = 1 },
            _ => { writes++; throw original; }, default));
        Assert.AreSame(original, ex);
        Assert.AreEqual(failDispose ? 1 : 0, f.Connection.Disposes);
        Assert.AreEqual(1, writes);
        Assert.AreEqual(0, f.Receipts.Inserts);
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.AreEqual(1, f.Connection.Rollbacks);
        AssertReleasedResources(f);
        if (loggerFailure is not null) AssertLoggerFailureWasReached(f, loggerFailure);
    }

    /// <summary>首次或重放已提交成功后，事务释放故障不得把确认结果变成失败。</summary>
    [TestMethod]
    [TestCategory("CleanupFailureRegression")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConfirmedCommit_DisposeFailureMustKeepSuccess(bool replay)
    {
        var f = new Fixture(prepared: true);
        var id = Guid.NewGuid();
        var expected = new Result(Guid.NewGuid(), 1);
        var writes = 0;
        Task<Result> Write(CancellationToken _) { writes++; return Task.FromResult(expected); }
        if (replay)
        {
            await f.Executor.ExecuteAsync(id, "Inventory.Inbound.Draft.Create", null, new { Value = 1 }, Write, default);
            f.Connection.Events.Clear();
        }
        f.Connection.FailTransactionDispose = true;
        var result = await f.Executor.ExecuteAsync(id, "Inventory.Inbound.Draft.Create", null, new { Value = 1 }, Write, default);
        Assert.AreEqual(expected, result.Value);
        Assert.AreEqual(1, f.Connection.Disposes);
        Assert.AreEqual(replay, result.IsReplay);
        Assert.AreEqual(1, writes);
        Assert.AreEqual(1, f.Connection.Events.Count(x => x == "Commit"));
        Assert.AreEqual(0, f.Connection.Rollbacks);
        AssertReleasedResources(f);
    }

    /// <summary>首次提交故障的诊断日志失败不能改变结果未知，也不能阻止回滚和释放。</summary>
    [TestMethod]
    [TestCategory("CleanupFailureRegression")]
    [DataRow("create")]
    [DataRow("write")]
    public async Task FirstCommitFailure_LoggingMustNotMaskOutcomeOrCleanup(string loggerFailure)
    {
        var f = new Fixture(prepared: true, loggerFailure: loggerFailure);
        f.Connection.FailCommit = true;
        var writes = 0;
        var ex = await Assert.ThrowsAsync<BusinessCommandException>(() => f.Executor.ExecuteAsync(
            Guid.NewGuid(), "Inventory.Inbound.Draft.Create", null, new { Value = 1 },
            _ => { writes++; return Task.FromResult(new Result(Guid.NewGuid(), 1)); }, default));
        Assert.AreEqual(ServiceErrorCodes.OutcomeUnknown, ex.Code);
        Assert.AreEqual(1, writes);
        Assert.AreEqual(1, f.Connection.Events.Count(x => x == "Commit"));
        Assert.AreEqual(1, f.Connection.Rollbacks);
        AssertReleasedResources(f);
        AssertLoggerFailureWasReached(f, loggerFailure);
    }

    /// <summary>确认故障注入的日志入口确实被调用，避免未到达异常路径时误报通过。</summary>
    private static void AssertLoggerFailureWasReached(Fixture fixture, string failure)
    {
        Assert.IsTrue(fixture.Connection.Events.Contains("LoggerCreate"));
        if (failure == "write") Assert.IsTrue(fixture.Connection.Events.Contains("LoggerWrite"));
    }

    /// <summary>验证清理尝试各执行一次且先释放事务；允许两次清理之间记录诊断日志。</summary>
    private static void AssertReleasedResources(Fixture fixture)
    {
        var events = fixture.Connection.Events;
        Assert.AreEqual(1, events.Count(x => x == "TransactionDispose"));
        Assert.AreEqual(1, events.Count(x => x == "LeaseDispose"));
        Assert.IsTrue(events.IndexOf("TransactionDispose") < events.IndexOf("LeaseDispose"));
        Assert.AreEqual(fixture.Connection.StartedTransactions, fixture.Connection.DisposedTransactions);
        Assert.IsNull(fixture.Session.Transaction);
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

        /// <summary>按需启用准备能力，复用原回执与事务替身，不模拟数据库持久化。</summary>
        public Fixture(bool prepared = false, string? loggerFailure = null)
        {
            Session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Session, Connection);
            Receipts = prepared ? new PreparedReceiptStub(Session, Connection) : new ReceiptStub(Session, Connection.Events);
            ILoggerFactory loggerFactory = loggerFailure is null ? NullLoggerFactory.Instance : new FailingLoggerFactory(Connection.Events, loggerFailure);
            Executor = new BusinessCommandExecutor(new ServiceBaseContext(Session, loggerFactory), Receipts, User);
        }
    }

    /// <summary>模拟已成功保存的回执及失败注入，不模拟 SQL Server 的锁和唯一索引。</summary>
    private class ReceiptStub(DbSession session, List<string> events) : ICommandReceiptRepository
    {
        public readonly Dictionary<Guid, CommandReceipt> Rows = [];
        public int LockResult, Inserts;
        public bool FailInsert;
        /// <summary>记录事务内取锁顺序，不提供真实数据库互斥。</summary>
        public Task<int> LockAsync(Guid commandId, CancellationToken token)
        {
            events.Add("Lock");
            Assert.IsNotNull(session.Transaction);
            return Task.FromResult(LockResult);
        }
        /// <summary>读取内存回执，只验证执行器分支。</summary>
        public Task<CommandReceipt?> FindAsync(Guid commandId, CancellationToken token)
        {
            events.Add("Find");
            return Task.FromResult(Rows.GetValueOrDefault(commandId));
        }
        /// <summary>记录追加调用；字典不随回滚撤销，不能证明真实持久化结果。</summary>
        public Task InsertAsync(CommandReceipt receipt, CancellationToken token)
        {
            events.Add("Insert");
            Assert.IsNotNull(session.Transaction);
            if (FailInsert) throw new InvalidOperationException("模拟回执写入失败");
            Rows.Add(receipt.CommandId, receipt);
            Inserts++;
            return Task.CompletedTask;
        }
    }

    /// <summary>为原回执替身添加可选准备能力，观察事务外获取与最终释放顺序。</summary>
    private sealed class PreparedReceiptStub : ReceiptStub, ICommandTransactionPreparation
    {
        private readonly DbSession _session;
        private readonly ConnectionStub _connection;
        /// <summary>模拟准备阶段竞争耗尽，返回空租约。</summary>
        public bool Busy;
        /// <summary>在返回已取得租约前注入取消。</summary>
        public Action? AfterAcquire;
        /// <summary>记录准备调用收到的原命令身份。</summary>
        public Guid LastCommandId;

        /// <summary>复用同一会话和原回执替身，避免复制命令夹具。</summary>
        public PreparedReceiptStub(DbSession session, ConnectionStub connection) : base(session, connection.Events)
        {
            _session = session;
            _connection = connection;
        }

        /// <summary>确认获取时尚无业务事务，并可注入繁忙或获取后的取消。</summary>
        public Task<IAsyncDisposable?> AcquireBeforeTransactionAsync(Guid commandId, CancellationToken token)
        {
            Assert.IsNull(_session.Transaction);
            token.ThrowIfCancellationRequested();
            _connection.Events.Add("Acquire");
            LastCommandId = commandId;
            if (Busy) return Task.FromResult<IAsyncDisposable?>(null);
            AfterAcquire?.Invoke();
            return Task.FromResult<IAsyncDisposable?>(new LeaseStub(_session, _connection));
        }

        /// <summary>释放时校验事务对象已先释放，且共享会话已清空事务引用。</summary>
        private sealed class LeaseStub(DbSession session, ConnectionStub connection) : IAsyncDisposable
        {
            /// <summary>记录释放事件，不模拟连接池或真实会话锁。</summary>
            public ValueTask DisposeAsync()
            {
                Assert.IsNull(session.Transaction);
                Assert.AreEqual(connection.StartedTransactions, connection.DisposedTransactions);
                connection.Events.Add("LeaseDispose");
                return ValueTask.CompletedTask;
            }
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

    /// <summary>在创建记录器或实际日志写入时注入故障，验证诊断不影响命令结果。</summary>
    private sealed class FailingLoggerFactory(List<string> events, string failure) : ILoggerFactory
    {
        /// <summary>记录诊断入口，并可模拟记录器创建失败。</summary>
        public ILogger CreateLogger(string categoryName)
        {
            events.Add("LoggerCreate");
            if (failure == "create") throw new InvalidOperationException("模拟记录器创建失败");
            return new FailingLogger(events);
        }
        /// <summary>夹具不注册外部日志提供器。</summary>
        public void AddProvider(ILoggerProvider provider) { }
        /// <summary>夹具不拥有日志资源。</summary>
        public void Dispose() { }

        /// <summary>仅观察诊断调用，不执行外部日志写入。</summary>
        private sealed class FailingLogger(List<string> events) : ILogger
        {
            /// <summary>不创建日志作用域。</summary>
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            /// <summary>允许日志扩展方法进入故障注入路径。</summary>
            public bool IsEnabled(LogLevel logLevel) => true;
            /// <summary>模拟日志提供器抛出异常。</summary>
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                events.Add("LoggerWrite");
                throw new InvalidOperationException("模拟日志写入失败");
            }
        }
    }

    /// <summary>记录事务提交与回滚，并允许模拟提交结果未知。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public int Commits, Rollbacks, Disposes;
        /// <summary>观察执行器调用顺序，不代表数据库执行轨迹。</summary>
        public readonly List<string> Events = [];
        /// <summary>计数真实创建的替身事务及其对象释放，开启失败不计入。</summary>
        public int StartedTransactions, DisposedTransactions;
        public IsolationLevel? LastRequestedIsolation;
        public bool FailCommit;
        /// <summary>可观察实例用于确认重放提交异常原样传播。</summary>
        public Exception? CommitFailure;
        /// <summary>在回滚调用已记录后注入清理故障。</summary>
        public bool FailRollback;
        /// <summary>在对象释放已记录后注入故障，不模拟真实连接资源回收。</summary>
        public bool FailTransactionDispose;
        /// <summary>在事务对象创建前注入故障。</summary>
        public bool FailBegin;
        [AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "test";
        public ConnectionState State => ConnectionState.Open;
        /// <summary>保留默认事务入口并记录开启顺序。</summary>
        public IDbTransaction BeginTransaction() => Begin(null);
        /// <summary>记录显式隔离级别，不模拟数据库隔离行为。</summary>
        public IDbTransaction BeginTransaction(IsolationLevel il) => Begin(il);
        /// <summary>在创建事务对象前注入开启失败，便于验证租约清理。</summary>
        private IDbTransaction Begin(IsolationLevel? isolation)
        {
            Events.Add("Begin");
            LastRequestedIsolation = isolation;
            if (FailBegin) throw new InvalidOperationException("模拟事务开启失败");
            StartedTransactions++;
            return new TransactionStub(this);
        }
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public void Close() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() { }
        public void Dispose() { Disposes++; }
        /// <summary>观察提交、回滚及对象释放，不模拟回执的真实事务性。</summary>
        private sealed class TransactionStub(ConnectionStub connection) : IDbTransaction
        {
            public IDbConnection Connection => connection;
            public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            /// <summary>记录提交尝试并保留既有未知结果故障注入。</summary>
            public void Commit()
            {
                connection.Events.Add("Commit");
                if (connection.CommitFailure is { } failure) throw failure;
                if (connection.FailCommit) throw new IOException("模拟提交连接中断");
                connection.Commits++;
            }
            /// <summary>只记录回滚调用，不宣称撤销内存回执。</summary>
            public void Rollback()
            {
                connection.Events.Add("Rollback");
                connection.Rollbacks++;
                if (connection.FailRollback) throw new InvalidOperationException("模拟回滚失败");
            }
            /// <summary>记录事务对象释放，供租约检查先后关系。</summary>
            public void Dispose()
            {
                connection.DisposedTransactions++;
                connection.Events.Add("TransactionDispose");
                if (connection.FailTransactionDispose) throw new InvalidOperationException("模拟事务释放失败");
            }
        }
    }
}
