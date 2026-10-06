using SwiftExport.Application.Interfaces.Systems.Numbering;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Execution;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>观察来源创建编排的独立事务、Link 重放和部分成功结果。</summary>
[TestClass]
public sealed class PurchaseDraftSourceOrchestrationTests
{
    /// <summary>已成功来源按 CommandItemId 重放，后续失败来源单独回滚，不清除已提交结果。</summary>
    [TestMethod]
    public async Task ReplayThenMissingSource_CommitAndRollbackSeparately()
    {
        var f = new Fixture();
        var command = f.Command();
        var result = await f.Service.CreateAsync(command);
        Assert.IsTrue(result.Success, result.Message);
        Assert.HasCount(2, result.Data.Sources);
        var replay = result.Data.Sources[0];
        Assert.AreEqual(NetDemandCommandOutcomeEnum.AlreadyApplied, replay.Outcome);
        Assert.HasCount(1, replay.PurchaseDrafts);
        Assert.AreEqual(f.Head.Uid, replay.PurchaseDrafts[0].PurchaseOrderId);
        var failed = result.Data.Sources[1];
        Assert.AreEqual(NetDemandCommandOutcomeEnum.Failed, failed.Outcome);
        Assert.AreEqual("SOURCE_NOT_FOUND", failed.ErrorCode);
        Assert.IsEmpty(failed.PurchaseDrafts);
        Assert.AreEqual(2, f.Connection.Begins);
        Assert.AreEqual(1, f.Connection.Commits);
        Assert.AreEqual(1, f.Connection.Rollbacks);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>同来源仅部分命令项存在 Link 时拒绝并回滚，不发布已有或未提交采购单。</summary>
    [TestMethod]
    public async Task PartialCommandItems_RollBackWithoutPublishingDrafts()
    {
        var f = new Fixture();
        var command = f.Command();
        var firstSource = command.Sources[0];
        firstSource.Items.Add(new CreatePurchaseDraftItemCommand
        {
            CommandItemId = Guid.NewGuid(), NetDemandId = Guid.NewGuid(), SupplierId = Guid.NewGuid(),
            ExpectedVersion = 1, ExpectedRemainingQty = 1, PurchaseQty = 1
        });
        command.Sources.RemoveAt(1);
        command.PreviewToken = firstSource.SourcePreviewToken;
        var result = await f.Service.CreateAsync(command);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual("IDEMPOTENCY_PARTIAL_STATE", result.Data.Sources.Single().ErrorCode);
        Assert.IsEmpty(result.Data.Sources.Single().PurchaseDrafts);
        Assert.AreEqual(1, f.Connection.Begins);
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.AreEqual(1, f.Connection.Rollbacks);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>各来源在原事务前整组准备，重放提交和后续来源回滚之后才释放，彼此不持有上一来源资源。</summary>
    [TestMethod]
    public async Task Preparation_PrecedesEachTransactionAndReleasesAfterReplayOrRollback()
    {
        var f = new Fixture(withPreparation: true); var command = f.Command();
        var result = await f.Service.CreateAsync(command);
        Assert.IsTrue(result.Success); Assert.HasCount(2, f.Preparations);
        for (var i = 0; i < 2; i++)
        {
            var source = command.Sources[i]; var prepared = f.Preparations[i];
            CollectionAssert.AreEqual(new[] { source.SourceUid }, prepared.Sources);
            CollectionAssert.AreEqual(source.Items.Select(x => x.NetDemandId).ToArray(), prepared.Nets);
            CollectionAssert.AreEqual(source.Items.Select(x => x.CommandItemId).ToArray(), prepared.Items);
            Assert.AreEqual(i, prepared.Begins);
        }
        Assert.AreEqual(NetDemandCommandOutcomeEnum.AlreadyApplied, result.Data.Sources[0].Outcome);
        Assert.AreEqual("SOURCE_NOT_FOUND", result.Data.Sources[1].ErrorCode);
        Assert.AreEqual(1, f.Connection.Commits); Assert.AreEqual(1, f.Connection.Rollbacks);
        Assert.AreEqual(2, f.Releases); Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>准备未成功时保留来源失败响应且不开事务，后续来源照常独立处理，不重试整个请求。</summary>
    [TestMethod]
    public async Task MissingPreparation_FailsOnlyItsSourceWithoutTransaction()
    {
        var f = new Fixture(withPreparation: true) { NullPreparationOnCall = 1 };
        var result = await f.Service.CreateAsync(f.Command());
        Assert.IsTrue(result.Success); Assert.AreEqual("UNEXPECTED_ERROR", result.Data.Sources[0].ErrorCode);
        Assert.AreEqual("创建采购草稿失败。", result.Data.Sources[0].Message);
        Assert.IsEmpty(result.Data.Sources[0].PurchaseDrafts);
        Assert.AreEqual("SOURCE_NOT_FOUND", result.Data.Sources[1].ErrorCode);
        Assert.HasCount(2, f.Preparations); Assert.AreEqual(1, f.Connection.Begins);
        Assert.AreEqual(0, f.Connection.Commits); Assert.AreEqual(1, f.Connection.Rollbacks);
        Assert.AreEqual(1, f.Releases); Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>事务前准备收到调用方取消继续传播，不开始来源事务、取号或处理后续来源。</summary>
    [TestMethod]
    public async Task CancelledPreparation_PropagatesBeforeAnyTransaction()
    {
        using var cancel = new CancellationTokenSource();
        var f = new Fixture(withPreparation: true);
        f.PrepareHandler = args => { cancel.Cancel(); return Task.FromCanceled<IAsyncDisposable?>((CancellationToken)args[4]!); };
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.CreateAsync(f.Command(), cancel.Token));
        Assert.HasCount(1, f.Preparations); Assert.AreEqual(0, f.Connection.Begins);
        Assert.AreEqual(0, f.Connection.Commits); Assert.AreEqual(0, f.Connection.Rollbacks);
        Assert.AreEqual(0, f.Releases); Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>已有父事务仍优先按原返回拒绝，不调用准备或接管调用方事务。</summary>
    [TestMethod]
    public async Task ExistingTransaction_IsRejectedBeforePreparation()
    {
        var f = new Fixture(withPreparation: true); using var parent = f.Connection.BeginTransaction();
        f.Session.Transaction = parent;
        try
        {
            var result = await f.Service.CreateAsync(f.Command());
            Assert.IsTrue(result.Success); Assert.IsTrue(result.Data.Sources.All(x => x.ErrorCode == "TRANSACTION_ALREADY_ACTIVE"));
            Assert.IsEmpty(f.Preparations); Assert.AreSame(parent, f.Session.Transaction);
            Assert.AreEqual(1, f.Connection.Begins); Assert.AreEqual(0, f.Connection.Commits); Assert.AreEqual(0, f.Connection.Rollbacks);
        }
        finally { f.Session.Transaction = null; }
    }

    /// <summary>仅增加准备能力的离线测试端口，不伪造PG物理租约或数据库事实。</summary>
    public interface IPreparedNetPort : IMaterialDemandDocumentRepository, IMaterialDemandTransactionPreparation { }

    /// <summary>只观察应用层归还时机，不能当作真实PG资源释放证明。</summary>
    private sealed class PreparationMarker(Action release) : IAsyncDisposable
    {
        /// <summary>记录父事务结束后的归还动作。</summary>
        public ValueTask DisposeAsync() { release(); return ValueTask.CompletedTask; }
    }

    /// <summary>用仓储替身只触发真实来源编排的重放与失败分支；任何新建或取号均失败。</summary>
    private sealed class Fixture
    {
        public readonly ConnectionStub Connection = new();
        public readonly DbSession Session;
        public readonly PurchaseDraftCreationService Service;
        public readonly PurchaseOrderHead Head = new()
        {
            Uid = Guid.NewGuid(), Version = 2, OrderNo = "PO-REPLAY",
            SupplierId = Guid.NewGuid(), SupplierCode = "S-1", SupplierName = "供应商"
        };
        /// <summary>记录每来源明确输入和当时事务开始次数，核对准备顺序。</summary>
        public readonly List<(Guid[] Sources, Guid[] Nets, Guid[] Items, int Begins)> Preparations = [];
        /// <summary>观察事务清空后的租约归还次数。</summary>
        public int Releases;
        /// <summary>指定一次准备返回空以验证来源失败边界；零表示不注入。</summary>
        public int NullPreparationOnCall;
        /// <summary>可注入准备取消，参数保持接口原输入，不提供数据库替身事实。</summary>
        public Func<object?[], Task<IAsyncDisposable?>>? PrepareHandler;
        private readonly Guid sourceId = new("00000000-0000-0000-0000-000000000001");
        private readonly Guid missingSourceId = new("00000000-0000-0000-0000-000000000002");
        private readonly Guid grossId = Guid.NewGuid();
        private readonly Guid demandId = Guid.NewGuid();
        private readonly Guid commandItemId = Guid.NewGuid();
        private readonly Guid requestId = Guid.NewGuid();

        /// <summary>组合既有重放/缺源事实与真实编排，可选择仅观察准备时序的端口。</summary>
        public Fixture(bool withPreparation = false)
        {
            Session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Session, Connection);
            var context = new ServiceBaseContext(Session, NullLoggerFactory.Instance);
            var gross = Proxy<IGrossDemandDocumentRepository>.New((method, args) => method.Name == "GetActiveBySourceForUpdateAsync"
                ? Task.FromResult<IReadOnlyList<GrossDemand>>((Guid)args[1]! == sourceId
                    ? [new GrossDemand { Uid = grossId }] : [])
                : throw new AssertFailedException(method.Name));
            Func<MethodInfo, object?[], object?> netHandler = (method, _) => method.Name == "GetActiveByGrossDemandForUpdateAsync"
                ? Task.FromResult<IReadOnlyList<NetDemand>>([new NetDemand { Uid = demandId, GrossDemandId = grossId }])
                : throw new AssertFailedException(method.Name);
            IMaterialDemandDocumentRepository net = withPreparation
                ? Proxy<IPreparedNetPort>.New((method, args) =>
                {
                    if (method.Name != "AcquireMaterialBeforeTransactionAsync") return netHandler(method, args);
                    Assert.IsNull(Session.Transaction); Assert.AreEqual(Connection.Begins, Releases);
                    Assert.AreEqual(0, ((IReadOnlyCollection<Guid>)args[1]!).Count);
                    Preparations.Add((((IReadOnlyCollection<Guid>)args[0]!).ToArray(),
                        ((IReadOnlyCollection<Guid>)args[2]!).ToArray(), ((IReadOnlyCollection<Guid>)args[3]!).ToArray(), Connection.Begins));
                    if (PrepareHandler is not null) return PrepareHandler(args);
                    return Task.FromResult<IAsyncDisposable?>(Preparations.Count == NullPreparationOnCall ? null :
                        new PreparationMarker(() => { Assert.IsNull(Session.Transaction); Releases++; }));
                })
                : Proxy<IMaterialDemandDocumentRepository>.New(netHandler);
            var links = Proxy<INetDemandExecutionLinkRepository>.New((method, _) => method.Name switch
            {
                "GetActiveByNetDemandIdsForUpdateAsync" =>
                    Task.FromResult<IReadOnlyList<PurchaseDemandCommitmentDto>>([]),
                "GetByCommandItemIdsForUpdateAsync" =>
                    Task.FromResult<IReadOnlyList<DemandExecutionLink>>([new DemandExecutionLink
                    {
                        ExecutionRequestId = requestId, CommandItemId = commandItemId,
                        NetDemandId = demandId, AllocatedQty = 1, TargetHeadId = Head.Uid
                    }]),
                _ => throw new AssertFailedException(method.Name)
            });
            var orders = Proxy<IPurchaseOrderDraftRepository>.New((method, _) => method.Name == "GetPurchaseDraftsByIdsAsync"
                ? Task.FromResult<IReadOnlyList<PurchaseOrderHead>>([Head])
                : throw new AssertFailedException("重放不得写采购草稿：" + method.Name));
            var numbers = Proxy<IDocumentNoService>.New((method, _) =>
                throw new AssertFailedException("重放不得取号：" + method.Name));
            var previewQuery = Proxy<IMaterialDemandQueryRepository>.New((method, _) => throw new AssertFailedException(method.Name));
            var preview = new PurchaseDraftPreviewService(context, previewQuery, orders,
                null!, null!, null!, null!, null!, null!, new PurchaseDraftPreviewBuilder());
            Service = new PurchaseDraftCreationService(context, gross, net, links, orders, numbers,
                preview, new PurchaseDraftPreviewBuilder(), new PurchaseDraftWriteModelBuilder());
        }

        public CreatePurchaseDraftsCommand Command() => new()
        {
            ExecutionRequestId = requestId, EntryMode = PurchaseDraftEntryModeEnum.ManualSelection,
            PreviewToken = "batch-token",
            Sources =
            [
                new CreatePurchaseDraftSourceCommand
                {
                    SourceType = "SO", SourceUid = sourceId, SourcePreviewToken = "source-token",
                    Items = [new CreatePurchaseDraftItemCommand
                    {
                        CommandItemId = commandItemId, NetDemandId = demandId, SupplierId = Head.SupplierId,
                        ExpectedVersion = 1, ExpectedRemainingQty = 1, PurchaseQty = 1
                    }]
                },
                new CreatePurchaseDraftSourceCommand
                {
                    SourceType = "SO", SourceUid = missingSourceId, SourcePreviewToken = "source-token",
                    Items = [new CreatePurchaseDraftItemCommand
                    {
                        CommandItemId = Guid.NewGuid(), NetDemandId = Guid.NewGuid(), SupplierId = Head.SupplierId,
                        ExpectedVersion = 1, ExpectedRemainingQty = 1, PurchaseQty = 1
                    }]
                }
            ]
        };
    }

    /// <summary>只响应已声明仓储方法，使意外查询或写入立即暴露。</summary>
    public class Proxy<T> : DispatchProxy where T : class
    {
        private Func<MethodInfo, object?[], object?> handler = default!;
        public static T New(Func<MethodInfo, object?[], object?> handler)
        { var instance = Create<T, Proxy<T>>(); ((Proxy<T>)(object)instance).handler = handler; return instance; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => handler(method!, args ?? []);
    }

    /// <summary>记录每个来源独立的开始、提交和回滚。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public int Begins, Commits, Rollbacks;
        [AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "stub";
        public ConnectionState State => ConnectionState.Open;
        public IDbTransaction BeginTransaction() { Begins++; return new TransactionStub(this); }
        public IDbTransaction BeginTransaction(IsolationLevel level) => BeginTransaction();
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public void Close() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() { }
        public void Dispose() { }
        private sealed class TransactionStub(ConnectionStub owner) : IDbTransaction
        {
            public IDbConnection Connection => owner;
            public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            public void Commit() => owner.Commits++;
            public void Rollback() => owner.Rollbacks++;
            public void Dispose() { }
        }
    }
}
