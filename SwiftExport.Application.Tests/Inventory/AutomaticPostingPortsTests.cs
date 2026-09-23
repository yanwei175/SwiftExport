using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Services.Inventory;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>使用真实库存内核与内存仓储验证内部编排；不冒充 SQL Server 事务与并发验收。</summary>
[TestClass]
public sealed class AutomaticPostingPortsTests
{
    /// <summary>同事务入库后出库使用新增余额，重放不再次取号或记账。</summary>
    [TestMethod]
    public async Task InboundThenOutboundAndReplayPreserveFacts()
    {
        var f = new Fixture();
        await f.Prepare();
        var received = await f.In.CreateAndPostAsync(f.InRequest);
        var issued = await f.Out.CreateAndPostAsync(f.OutRequest);
        Assert.AreEqual(0m, f.Balances.Values.Single().OnHandQty);
        Assert.AreEqual(2, f.Ledgers.Count);
        Assert.AreEqual(2, f.Numbers);
        Assert.AreEqual(f.InRequest.PackingListId, f.InHeaders.Values.Single().TriggerPackingListId);
        Assert.AreEqual(f.InRequest.Groups[0].Lines[0].BasisId, received.Documents[0].Lines[0].BasisId);
        Assert.IsNull(f.InLines.Single().OriginalLineId);
        var again = await f.In.CreateAndPostAsync(f.InRequest);
        Assert.IsTrue(again.AlreadyApplied);
        Assert.AreEqual(received.Documents[0].Lines[0].LedgerId, again.Documents[0].Lines[0].LedgerId);
        Assert.AreEqual(2, f.Numbers);
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.AreEqual(0, f.Connection.Rollbacks);
        Assert.AreEqual(1, issued.Documents[0].PostingStatus);
    }

    /// <summary>出库无采购字段，保留同产品不同依据；不足时整个事务回滚。</summary>
    [TestMethod]
    public async Task CustomerSuppliedNeedsNoPurchaseAndInsufficientStockRollsBack()
    {
        var f = new Fixture();
        f.OutRequest.Groups[0].OutboundType = OutboundDocumentTypeEnum.CustomerSuppliedConsumption;
        f.OutRequest.Groups[0].Lines[0].Quantity = 11;
        await f.Prepare();
        await f.In.CreateAndPostAsync(f.InRequest);
        var failure = await Assert.ThrowsAsync<AutomaticPostingException>(() => f.Out.CreateAndPostAsync(f.OutRequest));
        Assert.AreEqual("INVENTORY_BLOCKED", failure.Code);
        Assert.AreEqual(1, f.Connection.Rollbacks);
        Assert.IsNull(f.Session.Transaction);
        Assert.AreEqual(0, f.InHeaders.Count);
        Assert.AreEqual(0, f.Ledgers.Count);
    }

    /// <summary>同一采购行跨行数量汇总超过剩余容量时，在取号前失败。</summary>
    [TestMethod]
    public async Task PurchaseCapacityIsCheckedAcrossAllLines()
    {
        var f = new Fixture();
        f.InRequest.Groups[0].Lines = [f.InRequest.Groups[0].Lines[0], new()
        { LineId = Guid.NewGuid(), BasisId = Guid.NewGuid(), ProductId = f.ProductId, PurchaseLineId = f.PurchaseId, Unit = "PCS", Quantity = 1 }];
        await f.Prepare();
        var failure = await Assert.ThrowsAsync<AutomaticPostingException>(() => f.In.CreateAndPostAsync(f.InRequest));
        Assert.AreEqual("PURCHASE_CAPACITY_EXCEEDED", failure.Code);
        Assert.AreEqual(0, f.Numbers);
    }

    /// <summary>日志失败回滚之前单据及流水；不能只回滚最后一条写入。</summary>
    [TestMethod]
    public async Task LogFailureRollsBackEveryWrite()
    {
        var f = new Fixture { FailLog = true };
        await f.Prepare();
        var failure = await Assert.ThrowsAsync<AutomaticPostingException>(() => f.In.CreateAndPostAsync(f.InRequest));
        Assert.AreEqual("LOG_WRITE_FAILED", failure.Code);
        Assert.AreEqual(0, f.Ledgers.Count);
        Assert.AreEqual(0, f.InHeaders.Count);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>未准备事务或资源时不允许隐式建立事务，缺声明也不能在写入时补锁。</summary>
    [TestMethod]
    public async Task UnpreparedScopeAndUndeclaredKeysAreRejected()
    {
        var f = new Fixture();
        var failure = await Assert.ThrowsAsync<AutomaticPostingException>(() => f.In.CreateAndPostAsync(f.InRequest));
        Assert.AreEqual("RESOURCES_NOT_PREPARED", failure.Code);
        f = new Fixture();
        await f.Prepare();
        f.OutRequest.Groups[0].WarehouseSiteId = Guid.NewGuid();
        failure = await Assert.ThrowsAsync<AutomaticPostingException>(() => f.Out.CreateAndPostAsync(f.OutRequest));
        Assert.AreEqual("UNDECLARED_RESOURCE", failure.Code);
        Assert.AreEqual(0, f.Numbers);
    }

    /// <summary>明细身份相同但数量变化时拒绝整个批次重放。</summary>
    [TestMethod]
    public async Task ChangedCommandContentIsConflict()
    {
        var f = new Fixture();
        await f.Prepare();
        await f.In.CreateAndPostAsync(f.InRequest);
        f.InRequest.Groups[0].Lines[0].Quantity = 9;
        var failure = await Assert.ThrowsAsync<AutomaticPostingException>(() => f.In.CreateAndPostAsync(f.InRequest));
        Assert.AreEqual("COMMAND_CONFLICT", failure.Code);
    }

    /// <summary>单位大小写、空白及数量尾零属于同一规范化内容，不产生伪幂等冲突。</summary>
    [TestMethod]
    public async Task EquivalentNormalizedInputReplays()
    {
        var f = new Fixture();
        await f.Prepare();
        await f.In.CreateAndPostAsync(f.InRequest);
        f.InRequest.Groups[0].Lines[0].Quantity = 10.0000m;
        f.InRequest.Groups[0].Lines[0].Unit = " pcs ";
        Assert.IsTrue((await f.In.CreateAndPostAsync(f.InRequest)).AlreadyApplied);
        Assert.AreEqual(1, f.Numbers);
    }

    /// <summary>一张出库单允许两个同产品不同依据行，不能合并丢失来源。</summary>
    [TestMethod]
    public async Task DuplicateProductWithDifferentBasisKeepsBothLines()
    {
        var f = new Fixture();
        f.OutRequest.Groups[0].Lines[0].Quantity = 5;
        f.OutRequest.Groups[0].Lines = [f.OutRequest.Groups[0].Lines[0], new()
        { LineId = Guid.NewGuid(), BasisId = Guid.NewGuid(), ProductId = f.ProductId, Unit = "PCS", Quantity = 5 }];
        await f.Prepare();
        await f.In.CreateAndPostAsync(f.InRequest);
        var result = await f.Out.CreateAndPostAsync(f.OutRequest);
        Assert.AreEqual(2, result.Documents[0].Lines.Count);
        Assert.AreEqual(2, result.Documents[0].Lines.Select(x => x.BasisId).Distinct().Count());
    }

    /// <summary>自动入库拒绝客供件和错误供应商仓，不以自动化绕开采购资格。</summary>
    [TestMethod]
    public async Task CustomerSuppliedAutomaticReceiptIsRejected()
    {
        var f = new Fixture { CustomerSuppliedSource = true };
        await f.Prepare();
        var failure = await Assert.ThrowsAsync<AutomaticPostingException>(() => f.In.CreateAndPostAsync(f.InRequest));
        Assert.AreEqual("PURCHASE_NOT_RECEIVABLE", failure.Code);
    }

    /// <summary>正常反向先出后入，使用原流水并可重放，不重新扣增。</summary>
    [TestMethod]
    public async Task ReverseOriginalDocumentsAndReplay()
    {
        var f = new Fixture();
        await f.Prepare();
        await f.In.CreateAndPostAsync(f.InRequest);
        await f.Out.CreateAndPostAsync(f.OutRequest);
        f.NewTransaction();
        var inReverse = f.Reverse(f.InRequest.Groups[0].GroupId);
        var outReverse = f.Reverse(f.OutRequest.Groups[0].GroupId);
        await f.Prepare([inReverse.CommandId, outReverse.CommandId]);
        await f.Out.ReverseAsync(outReverse);
        await f.In.ReverseAsync(inReverse);
        Assert.AreEqual(0m, f.Balances.Values.Single().OnHandQty);
        Assert.AreEqual(4, f.Ledgers.Count);
        Assert.IsTrue((await f.In.ReverseAsync(inReverse)).AlreadyApplied);
        Assert.AreEqual(4, f.Ledgers.Count);
    }

    /// <summary>跨箱单反向不能凭原单身份绕过归属校验。</summary>
    [TestMethod]
    public async Task ReverseWrongPackingListIsBlocked()
    {
        var f = new Fixture();
        await f.Prepare();
        await f.In.CreateAndPostAsync(f.InRequest);
        f.InHeaders.Values.Single().TriggerPackingListId = Guid.NewGuid();
        f.NewTransaction();
        var reverse = f.Reverse(f.InRequest.Groups[0].GroupId);
        await f.Prepare([reverse.CommandId]);
        var failure = await Assert.ThrowsAsync<AutomaticPostingException>(() => f.In.ReverseAsync(reverse));
        Assert.AreEqual("DOCUMENT_STATE_INVALID", failure.Code);
    }

    /// <summary>不能先撤销已经被出库使用的自动入库，负库存异常不在本阶段放行。</summary>
    [TestMethod]
    public async Task ReverseInboundBeforeOutboundCannotGoNegative()
    {
        var f = new Fixture();
        await f.Prepare();
        await f.In.CreateAndPostAsync(f.InRequest);
        await f.Out.CreateAndPostAsync(f.OutRequest);
        f.NewTransaction();
        var reverse = f.Reverse(f.InRequest.Groups[0].GroupId);
        await f.Prepare([reverse.CommandId]);
        var failure = await Assert.ThrowsAsync<AutomaticPostingException>(() => f.In.ReverseAsync(reverse));
        Assert.AreEqual("INVENTORY_BLOCKED", failure.Code);
    }

    /// <summary>明细流水缺失时拒绝整单反向，不能按可找到的部分撤销。</summary>
    [TestMethod]
    public async Task MissingOriginalLedgerIsBlocked()
    {
        var f = new Fixture();
        await f.Prepare();
        await f.In.CreateAndPostAsync(f.InRequest);
        f.Ledgers.Clear();
        f.NewTransaction();
        var reverse = f.Reverse(f.InRequest.Groups[0].GroupId);
        await f.Prepare([reverse.CommandId]);
        var failure = await Assert.ThrowsAsync<AutomaticPostingException>(() => f.In.ReverseAsync(reverse));
        Assert.AreEqual("LEDGER_INTEGRITY", failure.Code);
    }

    /// <summary>在后续子命令取消时，之前入库不能留作可提交的半批次。</summary>
    [TestMethod]
    public async Task CancellationAbortsPriorInbound()
    {
        var f = new Fixture();
        await f.Prepare();
        await f.In.CreateAndPostAsync(f.InRequest);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Out.CreateAndPostAsync(f.OutRequest, cancellation.Token));
        Assert.IsNull(f.Session.Transaction);
        Assert.AreEqual(0, f.Ledgers.Count);
    }

    /// <summary>Schema 未准备时拒绝进入业务写入；不会自动执行迁移。</summary>
    [TestMethod]
    public async Task SchemaFailureDoesNotWrite()
    {
        var f = new Fixture { SchemaReady = false };
        var failure = await Assert.ThrowsAsync<AutomaticPostingException>(() => f.Prepare());
        Assert.AreEqual("SCHEMA_NOT_READY", failure.Code);
        Assert.AreEqual(0, f.Numbers);
    }

    /// <summary>只实现被测持久化契约的内存夹具，库存计算使用生产内核。</summary>
    private sealed class Fixture
    {
        public Guid WarehouseId = Guid.NewGuid(), ProductId = Guid.NewGuid(), SupplierId = Guid.NewGuid(), PurchaseId = Guid.NewGuid();
        public AutomaticPostingRequest InRequest = new(), OutRequest = new();
        public Dictionary<Guid, InboundDocument> InHeaders = [];
        public Dictionary<Guid, OutboundDocument> OutHeaders = [];
        public List<InboundDocumentLine> InLines = [];
        public List<OutboundDocumentLine> OutLines = [];
        public List<InventoryLedgerEntry> Ledgers = [];
        public Dictionary<(Guid, Guid), InventoryBalance> Balances = [];
        public Dictionary<Guid, BusinessOperationLog> Logs = [];
        public bool FailLog, CustomerSuppliedSource;
        public bool SchemaReady = true;
        public int Numbers;
        public readonly Connection Connection = new();
        public readonly DbSession Session;
        public IInboundPostingPort In = null!;
        public IOutboundPostingPort Out = null!;
        private AutomaticPostingScope _scope = null!;

        public Fixture()
        {
            Session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Session, Connection);
            Session.Transaction = Connection.BeginTransaction();
            Connection.Abort = () => { InHeaders.Clear(); OutHeaders.Clear(); InLines.Clear(); OutLines.Clear(); Ledgers.Clear(); Balances.Clear(); Logs.Clear(); };
            InRequest = Request(true); OutRequest = Request(false);
            OutRequest.CorrelationId = InRequest.CorrelationId; OutRequest.PackingListId = InRequest.PackingListId;
            Build();
        }

        public void NewTransaction() { Session.Transaction = Connection.BeginTransaction(); Build(); }
        public AutomaticReverseRequest Reverse(Guid id) => new() { CommandId = Guid.NewGuid(), CorrelationId = InRequest.CorrelationId,
            PackingListId = InRequest.PackingListId, BusinessDate = DateTime.Today, Reason = "测试反向", Documents = [new(id, 2)] };
        public Task Prepare(IReadOnlyList<Guid>? commands = null) => _scope.PrepareAsync(new()
        {
            CorrelationId = InRequest.CorrelationId, PackingListId = InRequest.PackingListId,
            CommandIds = commands ?? [InRequest.CommandId, OutRequest.CommandId], PurchaseLineIds = [PurchaseId],
            InboundDocumentIds = InRequest.Groups.Select(x => x.GroupId).ToArray(), OutboundDocumentIds = OutRequest.Groups.Select(x => x.GroupId).ToArray(),
            CommandItemIds = InRequest.Groups.Concat(OutRequest.Groups).SelectMany(x => x.Lines).Select(x => x.LineId).ToArray(),
            StockKeys = [new(WarehouseId, ProductId)]
        });
        private AutomaticPostingRequest Request(bool input) => new()
        {
            CommandId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(), PackingListId = Guid.NewGuid(), PackingListNo = "PL-TEST", BusinessDate = DateTime.Today,
            Groups = [new() { GroupId = Guid.NewGuid(), WarehouseSiteId = WarehouseId, OutboundType = input ? null : OutboundDocumentTypeEnum.FinalCustomerShipment,
                Lines = [new() { LineId = Guid.NewGuid(), BasisId = Guid.NewGuid(), ProductId = ProductId, Unit = "PCS", Quantity = 10, PurchaseLineId = input ? PurchaseId : null }] }]
        };

        private void Build()
        {
            var context = new ServiceBaseContext(Session, NullLoggerFactory.Instance);
            var wh = new WarehouseSite { Uid = WarehouseId, SupplierId = SupplierId, Code = "WH", Name = "仓库", IsActive = true };
            var product = new Product { Uid = ProductId, Sku = "SKU", Unit = "PCS" };
            var warehouse = Proxy<IRepository<WarehouseSite>>((_, _) => Task.FromResult<WarehouseSite?>(wh));
            var inventoryRepo = Proxy<IInventoryRepository>((m, a) => m switch
            {
                "AcquireCommandLockAsync" => Task.FromResult(0),
                "LockLedgerByCommandItemAsync" => Task.FromResult(Ledgers.SingleOrDefault(x => x.CommandItemId == (Guid)a[0]!)),
                "LockLedgerByIdAsync" => Task.FromResult(Ledgers.SingleOrDefault(x => x.Uid == (Guid)a[0]!)),
                "LockReversalByOriginalAsync" => Task.FromResult(Ledgers.SingleOrDefault(x => x.ReversalOfLedgerId == (Guid)a[0]!)),
                "LockReplacementByOriginalAsync" => Task.FromResult<InventoryLedgerEntry?>(null),
                "GetProductForPostingAsync" => Task.FromResult<Product?>(product),
                "LockBalanceAsync" => Task.FromResult(Balances.GetValueOrDefault(((Guid)a[0]!, (Guid)a[1]!))),
                "InsertBalanceAsync" => InsertBalance((InventoryBalance)a[0]!),
                "UpdateBalanceAsync" => Task.FromResult(1),
                "NextLedgerSequenceAsync" => Task.FromResult((long)Ledgers.Count + 1),
                "InsertLedgerAsync" => InsertLedger((InventoryLedgerEntry)a[0]!),
                _ => throw new NotSupportedException(m)
            });
            var inRepo = Proxy<IInboundManagerRepository>((m, a) => m switch
            {
                "LockPurchaseSourcesAsync" => Task.CompletedTask,
                "LockHeaderAsync" => Task.FromResult(InHeaders.GetValueOrDefault((Guid)a[0]!)),
                "LockLinesAsync" => Task.FromResult<IReadOnlyList<InboundDocumentLine>>(InLines.Where(x => x.InboundDocumentId == (Guid)a[0]!).ToList()),
                "ReadReverseLedgersAsync" => History((Guid)a[0]!),
                "HasUnprovenReceiptAsync" => Task.FromResult(false),
                "ReadPostingSourcesAsync" => Task.FromResult<IReadOnlyList<InboundPurchasePostingSourceDto>>([new() { SourceLineId = PurchaseId,
                    ProductId = ProductId, IsReceivable = true, IsCustomerSupplied = CustomerSuppliedSource, OriginalQty = 10,
                    PostedNetQty = InHeaders.Values.Where(x => x.PostingStatus == InboundPostingStatusEnum.Posted).Sum(h => InLines.Where(l => l.InboundDocumentId == h.Uid).Sum(l => l.Quantity)), Unit = "PCS", CurrentUnit = "PCS" }]),
                _ => throw new NotSupportedException(m)
            });
            var outRepo = Proxy<IOutboundManagerRepository>((m, a) => m switch
            {
                "LockHeaderAsync" => Task.FromResult(OutHeaders.GetValueOrDefault((Guid)a[0]!)),
                "LockLinesAsync" => Task.FromResult<IReadOnlyList<OutboundDocumentLine>>(OutLines.Where(x => x.OutboundDocumentId == (Guid)a[0]!).ToList()),
                "ReadReverseLedgersAsync" => History((Guid)a[0]!),
                _ => throw new NotSupportedException(m)
            });
            var auto = Proxy<IAutomaticDocumentRepository>((m, a) => m switch
            {
                "IsSchemaReadyAsync" => Task.FromResult(SchemaReady), "LockProductsAsync" => Task.CompletedTask,
                "GetPurchaseSupplierAsync" => Task.FromResult<Guid?>(SupplierId),
                "InsertInboundAsync" => InsertIn((InboundDocument)a[0]!, (IReadOnlyList<InboundDocumentLine>)a[1]!),
                "InsertOutboundAsync" => InsertOut((OutboundDocument)a[0]!, (IReadOnlyList<OutboundDocumentLine>)a[1]!),
                "SetInboundStateAsync" => State((InboundDocument)a[0]!, (bool)a[1]!),
                "SetOutboundStateAsync" => State((OutboundDocument)a[0]!, (bool)a[1]!),
                _ => throw new NotSupportedException(m)
            });
            _scope = new(context, auto, inRepo, outRepo, inventoryRepo, warehouse);
            var userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var ports = new AutomaticPostingPorts(_scope, auto, inRepo, outRepo,
                new InventoryPostingService(context, inventoryRepo, Proxy<IWarehouseSiteRepository>((_, _) => Task.FromResult<WarehouseSite?>(wh))),
                warehouse, inventoryRepo, Proxy<IBusinessOperationLogRepository>((m, a) => m switch
                {
                    "GetByModuleCommandAsync" => Task.FromResult(Logs.GetValueOrDefault((Guid)a[1]!)),
                    "InsertAsync" => Log((BusinessOperationLog)a[0]!), _ => throw new NotSupportedException(m)
                }), Proxy<IDocumentNoService>((_, _) => Task.FromResult(ServiceResult<DocumentNoResponse>.Ok(new() { DocumentNo = "AUTO-" + ++Numbers }))),
                Proxy<ICurrentUser>((m, _) => m switch { "get_IsAuthenticated" => true, "get_UserId" => (Guid?)userId, "get_UserName" => "测试", _ => throw new NotSupportedException(m) }));
            In = ports; Out = ports;
        }
        private Task<IReadOnlyList<InventoryLedgerEntry>> History(Guid id) => Task.FromResult<IReadOnlyList<InventoryLedgerEntry>>(Ledgers.Where(x => x.SourceDocumentId == id).ToList());
        private Task<int> InsertBalance(InventoryBalance b) { b.Version = 1; Balances.Add((b.WarehouseSiteId, b.ProductId), b); return Task.FromResult(1); }
        private Task<int> InsertLedger(InventoryLedgerEntry e) { Ledgers.Add(e); return Task.FromResult(1); }
        private Task InsertIn(InboundDocument h, IReadOnlyList<InboundDocumentLine> l) { h.Version = 1; InHeaders.Add(h.Uid, h); InLines.AddRange(l); return Task.CompletedTask; }
        private Task InsertOut(OutboundDocument h, IReadOnlyList<OutboundDocumentLine> l) { h.Version = 1; OutHeaders.Add(h.Uid, h); OutLines.AddRange(l); return Task.CompletedTask; }
        private static Task State(InboundDocument h, bool reverse) { h.Version++; h.PostingStatus = reverse ? InboundPostingStatusEnum.Reversed : InboundPostingStatusEnum.Posted; return Task.CompletedTask; }
        private static Task State(OutboundDocument h, bool reverse) { h.Version++; h.PostingStatus = reverse ? OutboundPostingStatusEnum.Reversed : OutboundPostingStatusEnum.Posted; return Task.CompletedTask; }
        private Task<int> Log(BusinessOperationLog log) { if (FailLog) return Task.FromResult(0); Logs.Add(log.CommandId, log); return Task.FromResult(1); }
    }

    private static T Proxy<T>(Func<string, object?[], object?> handler) where T : class
    { var proxy = DispatchProxy.Create<T, MethodProxy>(); ((MethodProxy)(object)proxy).Handler = handler; return proxy; }
    /// <summary>只处理夹具显式列出的仓储方法，未预期调用立即失败。</summary>
    public class MethodProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!.Name, args ?? []);
    }
    /// <summary>只验证服务不提交和失败会回滚；不模拟 SQL Server 锁。</summary>
    private sealed class Connection : IDbConnection
    {
        public int Commits, Rollbacks;
        public Action? Abort;
        [AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "UnitTest";
        public ConnectionState State => ConnectionState.Open;
        public IDbTransaction BeginTransaction() => new Transaction(this);
        public IDbTransaction BeginTransaction(IsolationLevel il) => BeginTransaction();
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public void Close() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() { }
        public void Dispose() { }
        private sealed class Transaction(Connection connection) : IDbTransaction
        {
            private bool _ended;
            public IDbConnection Connection => connection;
            public IsolationLevel IsolationLevel => IsolationLevel.Serializable;
            public void Commit() { if (_ended) throw new InvalidOperationException(); _ended = true; connection.Commits++; }
            public void Rollback() { if (_ended) return; _ended = true; connection.Rollbacks++; connection.Abort?.Invoke(); }
            public void Dispose() { }
        }
    }
}
