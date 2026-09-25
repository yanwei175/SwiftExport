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
        private readonly Guid sourceId = new("00000000-0000-0000-0000-000000000001");
        private readonly Guid missingSourceId = new("00000000-0000-0000-0000-000000000002");
        private readonly Guid grossId = Guid.NewGuid();
        private readonly Guid demandId = Guid.NewGuid();
        private readonly Guid commandItemId = Guid.NewGuid();
        private readonly Guid requestId = Guid.NewGuid();

        public Fixture()
        {
            Session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Session, Connection);
            var context = new ServiceBaseContext(Session, NullLoggerFactory.Instance);
            var gross = Proxy<IGrossDemandDocumentRepository>.New((method, args) => method.Name == "GetActiveBySourceForUpdateAsync"
                ? Task.FromResult<IReadOnlyList<GrossDemand>>((Guid)args[1]! == sourceId
                    ? [new GrossDemand { Uid = grossId }] : [])
                : throw new AssertFailedException(method.Name));
            var net = Proxy<IMaterialDemandDocumentRepository>.New((method, _) => method.Name == "GetActiveByGrossDemandForUpdateAsync"
                ? Task.FromResult<IReadOnlyList<NetDemand>>([new NetDemand { Uid = demandId, GrossDemandId = grossId }])
                : throw new AssertFailedException(method.Name));
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
