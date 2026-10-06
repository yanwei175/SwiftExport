using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.SourceExit;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Exceptions;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Models;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Application.Services.DemandPlanning.Sources.Reception;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Auditing;
using SwiftExport.Infrastructure.Repositories.Commands;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Document;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>实际来源接收、PG毛需求/业务审计/机器回执及父执行器联验；未迁下游端口仅用于禁止意外调用，不冒称完整售后/物料链。</summary>
[TestClass, DoNotParallelize, TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlSourceReceptionCommandIntegrationTests
{
    /// <summary>业务或回执保存失败时，已经真实写入的两Gross及来源审计整体回滚，并释放两种租约。</summary>
    [TestMethod]
    [DataRow("action")]
    [DataRow("receipt")]
    public async Task ActualReception_AuditAndGrossRollBackWithParentCommand(string failure)
    {
        using var f = new Fixture();
        f.Receipts.FailInsert = failure == "receipt";
        var error = await Assert.ThrowsAsync<InjectedFailure>(() => f.Execute(async _ =>
        {
            var result = await f.Reception.ReceiveAsync(f.Command(twoLines: true));
            Assert.AreEqual(2, result.CreatedCount);
            Assert.AreEqual(2, await f.Count("gross_demand", "source_uid", f.Source));
            Assert.AreEqual(1, await f.Count("business_operation_log", "command_id", f.CommandId));
            Assert.AreEqual(0, await f.Count("command_receipt", "command_id", f.CommandId));
            if (failure == "action") throw f.Failure;
            return result.CreatedCount;
        }));
        Assert.AreSame(f.Failure, error);
        Assert.AreEqual(failure == "receipt" ? 1 : 0, f.Receipts.InsertCalls);
        Assert.AreEqual(0, f.Exits.Calls);
        await f.AssertClean();
    }

    /// <summary>同版本内容冲突仍是原来源业务异常；前一次真实新增及审计不能留在失败父命令之外。</summary>
    [TestMethod]
    [DataRow("SO")]
    [DataRow("CR")]
    public async Task SameSourceVersionChangedContent_PreservesOriginalErrorAndAtomicRollback(string type)
    {
        using var f = new Fixture();
        var error = await Assert.ThrowsAsync<SourceDemandReceptionException>(() => f.Execute(async _ =>
        {
            var first = f.Command(type: type); Assert.AreEqual(1, (await f.Reception.ReceiveAsync(first)).CreatedCount);
            var changed = f.Command(type: type); changed.Items[0].DemandQty = 2;
            await f.Reception.ReceiveAsync(changed);
            return 0;
        }));
        Assert.AreEqual("SOURCE_VERSION_CONFLICT", error.Code);
        Assert.AreEqual(0, f.Receipts.InsertCalls);
        await f.AssertClean();
    }

    /// <summary>另一个来源持有者阻塞准备，取消时首次回执/业务事务/业务委托都未开始；只用真实PG服务端查询取证。</summary>
    [TestMethod]
    public async Task SourcePreparationCancellation_PrecedesFirstReceiptAndBusinessSnapshot()
    {
        using var f = new Fixture(); using var holder = Session(); using var observer = Session();
        var held = await new PostgreSqlGrossDemandDocumentRepository(Context(holder)).AcquireBeforeTransactionAsync([f.Source], []);
        Assert.IsNotNull(held);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var beginPreparation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pid = ((NpgsqlConnection)f.Session.Connection).ProcessID;
        var called = 0;
        var operation = f.Executor.ExecuteAsync(f.CommandId, "Migration.SourceReception", f.Source, new { Value = 1 },
            _ => { called++; return Task.FromResult(1); }, deadline.Token, IsolationLevel.Serializable,
            beforeTransaction: async token =>
            {
                entered.TrySetResult(); await beginPreparation.Task.WaitAsync(token);
                return await f.Reception.AcquireBeforeTransactionAsync([f.Source], [], token);
            });
        try
        {
            await entered.Task.WaitAsync(deadline.Token);
            var commandStart = await observer.Connection.ExecuteScalarAsync<DateTime>(
                "SELECT query_start FROM pg_stat_activity WHERE pid=@Pid", new { Pid = pid });
            beginPreparation.TrySetResult();
            while (!await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid=@Pid
                    AND query LIKE '%pg_try_advisory_lock%' AND query_start>@Start)
                """, new { Pid = pid, Start = commandStart }, cancellationToken: deadline.Token)))
                await Task.Delay(25, deadline.Token);
            Assert.IsFalse(operation.IsCompleted); Assert.IsNull(f.Session.Transaction);
            Assert.AreEqual(0, f.Receipts.LockCalls); Assert.AreEqual(0, f.Receipts.FindCalls); Assert.AreEqual(0, called);
            deadline.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (await observer.Connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM pg_locks WHERE pid=@Pid AND locktype='advisory'", new { Pid = pid }) != 0)
                await Task.Delay(25, cleanup.Token);
            Assert.IsNull(f.Session.Transaction); Assert.AreEqual(0, called);
        }
        finally
        {
            deadline.Cancel(); beginPreparation.TrySetResult();
            try { await operation; } catch (OperationCanceledException) { }
            finally { await held.DisposeAsync(); }
        }
    }

    /// <summary>固定故障库门禁，所有实例独立原生连接；不初始化或改变结构。</summary>
    private static DbSession Session() => new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
    /// <summary>实际仓储使用同一会话和审计用户，不建立第二业务连接。</summary>
    private static RepositoryBaseContext Context(DbSession session)
    { var user = new User(); return new(session, NullLoggerFactory.Instance, new AuditStampService(user), user); }

    /// <summary>来源、回执及日志随机身份，所有业务写入在故意失败的父事务内回滚，不提交持久夹具。</summary>
    private sealed class Fixture : IDisposable
    {
        internal DbSession Session { get; } = PostgreSqlSourceReceptionCommandIntegrationTests.Session();
        internal Guid Source { get; } = Guid.NewGuid();
        internal Guid CommandId { get; } = Guid.NewGuid();
        private Guid Line { get; } = Guid.NewGuid();
        private Guid OtherLine { get; } = Guid.NewGuid();
        internal InjectedFailure Failure { get; } = new();
        internal UnusedExits Exits { get; } = new();
        internal SourceDemandReceptionService Reception { get; }
        internal ReceiptProbe Receipts { get; }
        internal BusinessCommandExecutor Executor { get; }
        /// <summary>Receiver、Gross、日志和机器回执全部实际实现；退出端口在新建路径不应被调用。</summary>
        internal Fixture()
        {
            var user = new User(); var c = new RepositoryBaseContext(Session, NullLoggerFactory.Instance, new AuditStampService(user), user);
            Reception = new(Session, new PostgreSqlGrossDemandDocumentRepository(c), Exits, Exits,
                new PostgreSqlBusinessOperationLogRepository(c), new Correlation(), user);
            Receipts = new(new PostgreSqlCommandReceiptRepository(c), Failure);
            Executor = new(new ServiceBaseContext(Session, NullLoggerFactory.Instance), Receipts, user);
        }
        /// <summary>准备明确发生在Serializable首次回执前，原业务输入和委托保持父执行器契约。</summary>
        internal Task<CommandExecutionResult<int>> Execute(Func<CancellationToken, Task<int>> action) =>
            Executor.ExecuteAsync(CommandId, "Migration.SourceReception", Source, new { Value = 1 }, action,
                default, IsolationLevel.Serializable, beforeTransaction: token => Reception.AcquireBeforeTransactionAsync([Source], [], token));
        /// <summary>合法非标来源不需要产品引用；同源行身份在两次请求中一致。</summary>
        internal SourceDemandReceptionCommand Command(bool twoLines = false, string type = "SO") => new()
        {
            CommandId = CommandId, SourceUid = Source, SourceType = type, SourceNo = "PGSRC" + Source.ToString("N"),
            SourceVersion = 1, CustomerId = Guid.NewGuid(), RejectUnsubmittedActiveLines = true,
            Items = twoLines ? [Item(Line, 1), Item(OtherLine, 2)] : [Item(Line, 1)]
        };
        /// <summary>当前测试行全部正量并有单位，满足原来源准入及PG数量/产品互斥约束。</summary>
        private static SourceDemandReceptionItem Item(Guid uid, int number) => new()
        { SourceLineId = uid, SourceLineNumber = number, IsNonStandard = true, SkuSnapshot = "非标测试", UnitSnapshot = "PCS", DemandQty = 1 };
        /// <summary>固定本类可信表/列名与随机身份，在当前父事务内观察真实写入。</summary>
        internal Task<int> Count(string table, string column, Guid uid) => Session.Connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*)::integer FROM public.{table} WHERE {column}=@Uid", new { Uid = uid }, Session.Transaction);
        /// <summary>父事务结束后同一连接可用、三类事实为零、租约无残留。</summary>
        internal async Task AssertClean()
        {
            Assert.IsNull(Session.Transaction); Assert.AreEqual(ConnectionState.Open, Session.Connection.State);
            Assert.AreEqual(0, await Count("gross_demand", "source_uid", Source));
            Assert.AreEqual(0, await Count("business_operation_log", "command_id", CommandId));
            Assert.AreEqual(0, await Count("command_receipt", "command_id", CommandId));
            Assert.AreEqual(0L, await Session.Connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM pg_locks WHERE pid=pg_backend_pid() AND locktype='advisory'"));
            Assert.AreEqual(1, await Session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
        }
        /// <summary>外层负责会话关闭，生产执行器应已清掉本例事务和租约。</summary>
        public void Dispose() => Session.Dispose();
    }

    /// <summary>转发真实回执读取和准备；仅指定Insert失败用于证明业务及来源日志的父事务原子性。</summary>
    private sealed class ReceiptProbe(PostgreSqlCommandReceiptRepository inner, InjectedFailure failure)
        : ICommandReceiptRepository, ICommandTransactionPreparation
    {
        internal bool FailInsert { get; set; }
        internal int LockCalls { get; private set; }
        internal int FindCalls { get; private set; }
        internal int InsertCalls { get; private set; }
        /// <summary>真实同CommandId事务前租约，不返回模拟锁结果。</summary>
        public Task<IAsyncDisposable?> AcquireBeforeTransactionAsync(Guid id, CancellationToken token) => inner.AcquireBeforeTransactionAsync(id, token);
        /// <summary>原事务内Lock完整转发，并计数首次事务边界。</summary>
        public Task<int> LockAsync(Guid id, CancellationToken token) { LockCalls++; return inner.LockAsync(id, token); }
        /// <summary>实际PG回执读取，禁止伪造历史重放。</summary>
        public Task<CommandReceipt?> FindAsync(Guid id, CancellationToken token) { FindCalls++; return inner.FindAsync(id, token); }
        /// <summary>在真实业务完成后指定异常，普通路径完整转发。</summary>
        public Task InsertAsync(CommandReceipt receipt, CancellationToken token) { InsertCalls++; if (FailInsert) throw failure; return inner.InsertAsync(receipt, token); }
    }
    /// <summary>明确的测试故障，不冒充数据库错误或改变生产分类。</summary>
    private sealed class InjectedFailure : Exception;
    /// <summary>固定有效用户身份，实际来源准入不借用现有用户数据。</summary>
    private sealed class User : ICurrentUser
    { public Guid? UserId { get; } = Guid.NewGuid(); public string UserName => "__pgtest_source_receiver__"; public bool IsAuthenticated => true; }
    /// <summary>固定请求关联标识，日志使用真实当前上下文。</summary>
    private sealed class Correlation : ICorrelationContext { public string CorrelationId => "PG-source-reception"; }
    /// <summary>新建不涉及下游退出，任何调用都是测试失败；这些端口未作PG替换，不声称已验证。</summary>
    private sealed class UnusedExits : IMaterialDemandSourceExitService, ISourceDeliveryService
    {
        internal int Calls { get; private set; }
        /// <summary>原新建路径不应读取活动物料。</summary>
        public Task<bool> HasActiveAsync(Guid id) { Calls++; throw new AssertFailedException("新建不应读取退出物料。"); }
        /// <summary>原新建路径不应评估物料历史。</summary>
        public Task<IReadOnlyDictionary<Guid, MaterialDemandSourceExitAssessment>> AssessSourceExitAsync(IEnumerable<Guid> ids) { Calls++; throw new AssertFailedException("新建不应评估退出历史。"); }
        /// <summary>本测试不验证交付搜索。</summary>
        public Task<ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>> SearchAsync(SourceDemandDeliverySearchRequest request, CancellationToken token = default) => throw new AssertFailedException();
        /// <summary>本测试不验证交付详情。</summary>
        public Task<ServiceResult<SourceDemandDeliveryItemDto>> GetAsync(Guid id, CancellationToken token = default) => throw new AssertFailedException();
        /// <summary>本测试不验证装箱交付声明。</summary>
        public Task<IReadOnlyDictionary<Guid, SourceDemandDeliveryItemDto>> VerifyForUpdateAsync(IReadOnlyCollection<SourceDeliveryClaim> claims, CancellationToken token = default) => throw new AssertFailedException();
        /// <summary>本测试不验证来源结案。</summary>
        public Task VerifyClosureAsync(string type, Guid source, IReadOnlyDictionary<Guid, decimal> expected, CancellationToken token = default) => throw new AssertFailedException();
        /// <summary>原新建来源不应校验已有交付退出。</summary>
        public Task VerifySourceExitAsync(string type, Guid source, IReadOnlyCollection<Guid> ids, CancellationToken token = default) { Calls++; throw new AssertFailedException("新建不应调用交付退出。"); }
    }
}
