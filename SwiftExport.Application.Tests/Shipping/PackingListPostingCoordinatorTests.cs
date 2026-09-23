using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>真实协调器配合严格接口替身验证调用和回滚请求，不冒充数据库原子性验证。</summary>
[TestClass]
public sealed class PackingListPostingCoordinatorTests
{
    /// <summary>无库存交付走通正向、反向及原命令重放，始终不调用库存端口。</summary>
    [TestMethod]
    public async Task DeliveryOnlyPostReverseAndReplay()
    {
        var f = new Fixture();
        var preview = await f.Service.PreviewAsync(f.Head.Uid, 1, f.Date, default);
        var command = new PackingListPostRequest(f.Head.Uid, Guid.NewGuid(), 1, f.Date, preview.Token!);
        await f.Service.PostAsync(command, default);
        Assert.AreEqual(PackingListPostingStatusEnum.Posted, f.Head.PostingStatus);
        Assert.IsTrue(f.Events.IndexOf("sources") < f.Events.IndexOf("head"));
        Assert.IsTrue((await f.Service.PostAsync(command, default)).AlreadyApplied);
        Assert.AreEqual(1, f.Executions.Count);
        var reverse = await f.Service.ReversePreviewAsync(f.Head.Uid, f.Head.Version, f.Date, "录单错误", default);
        Assert.AreEqual(0, reverse.Blockers.Count);
        var reverseCommand = new PackingListReverseRequest(f.Head.Uid, Guid.NewGuid(), f.Head.Version, f.Date, "录单错误", reverse.Token!);
        await f.Service.ReverseAsync(reverseCommand, default);
        Assert.AreEqual(PackingListPostingStatusEnum.Reversed, f.Head.PostingStatus);
        Assert.AreEqual(f.Executions[0].Uid, f.Executions[1].OriginalExecutionId);
        Assert.AreEqual(f.Evidence[0].Uid, f.Evidence[1].OriginalExecutionLineId);
        Assert.IsTrue((await f.Service.ReverseAsync(reverseCommand, default)).AlreadyApplied);
        Assert.AreEqual(4, f.Commits);
        Assert.AreEqual(0, f.Rollbacks);
    }

    /// <summary>日志失败不提交，并清除共享事务；这里只验证回滚调用。</summary>
    [TestMethod]
    public async Task LogFailureRollsBack()
    {
        var f = new Fixture { FailLog = true };
        var preview = await f.Service.PreviewAsync(f.Head.Uid, 1, f.Date, default);
        var error = await Assert.ThrowsExactlyAsync<PackingListPostingFailure>(() => f.Service.PostAsync(new(f.Head.Uid, Guid.NewGuid(), 1, f.Date, preview.Token!), default));
        Assert.AreEqual("LOG_FAILED", error.Code);
        Assert.AreEqual(0, f.Commits);
        Assert.AreEqual(1, f.Rollbacks);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>过期预览以及同命令不同内容不产生第二份证据。</summary>
    [TestMethod]
    public async Task RejectsStalePreviewAndConflictingReplay()
    {
        var f = new Fixture();
        var stale = await Assert.ThrowsExactlyAsync<PackingListPostingFailure>(() => f.Service.PostAsync(new(f.Head.Uid, Guid.NewGuid(), 1, f.Date, "stale"), default));
        Assert.AreEqual("PREVIEW_STALE", stale.Code);
        Assert.AreEqual(0, f.Executions.Count);
        var preview = await f.Service.PreviewAsync(f.Head.Uid, 1, f.Date, default);
        var command = new PackingListPostRequest(f.Head.Uid, Guid.NewGuid(), 1, f.Date, preview.Token!);
        await f.Service.PostAsync(command, default);
        var conflict = await Assert.ThrowsExactlyAsync<PackingListPostingFailure>(() => f.Service.PostAsync(command with { BusinessDate = f.Date.AddDays(1) }, default));
        Assert.AreEqual("COMMAND_CONFLICT", conflict.Code);
        Assert.AreEqual(1, f.Executions.Count);
    }

    /// <summary>已成功命令重放只读取原结果，不依赖可能失效的当前来源和库存。</summary>
    [TestMethod]
    public async Task ReplayDoesNotReadCurrentBusinessFacts()
    {
        var f = new Fixture();
        var preview = await f.Service.PreviewAsync(f.Head.Uid, 1, f.Date, default);
        var post = new PackingListPostRequest(f.Head.Uid, Guid.NewGuid(), 1, f.Date, preview.Token!);
        await f.Service.PostAsync(post, default);
        var reversePreview = await f.Service.ReversePreviewAsync(f.Head.Uid, f.Head.Version, f.Date, "更正", default);
        var reverse = new PackingListReverseRequest(f.Head.Uid, Guid.NewGuid(), f.Head.Version, f.Date, "更正", reversePreview.Token!);
        await f.Service.ReverseAsync(reverse, default);
        f.RejectCurrentReads = true;
        Assert.IsTrue((await f.Service.PostAsync(post, default)).AlreadyApplied);
        Assert.IsTrue((await f.Service.ReverseAsync(reverse, default)).AlreadyApplied);
        var conflict = await Assert.ThrowsExactlyAsync<PackingListPostingFailure>(() =>
            f.Service.PostAsync(post with { BusinessDate = f.Date.AddDays(1) }, default));
        Assert.AreEqual("COMMAND_CONFLICT", conflict.Code);
        Assert.AreEqual(2, f.Executions.Count);
    }

    /// <summary>普通成品缺口先自动收货，再与无需采购 Link 的客供件一起出库，并保存全部关联。</summary>
    [TestMethod]
    public async Task InventoryPostPreparesReceivesIssuesAndPersistsEvidence()
    {
        var f = new Fixture(inventory: true);
        var preview = await f.Service.PreviewAsync(f.Head.Uid, 1, f.Date, default);
        Assert.AreEqual(0, preview.Blockers.Count);
        var command = new PackingListPostRequest(f.Head.Uid, Guid.NewGuid(), 1, f.Date, preview.Token!);
        var result = await f.Service.PostAsync(command, default);
        CollectionAssert.AreEqual(new[] { "prepare", "inbound", "outbound", "mark", "append", "log", "commit" },
            f.Events.Where(x => x is "prepare" or "inbound" or "outbound" or "mark" or "append" or "log" or "commit").ToArray());
        Assert.AreEqual(3, f.Evidence.Count);
        Assert.AreEqual(3, result.Documents.Count);
        Assert.AreEqual(3, f.Evidence.Select(x => x.LedgerId).Distinct().Count());
        Assert.AreEqual(5m, f.Evidence.Single(x => x.ActionKind == "Receipt").Qty);
        Assert.AreEqual(2m, f.Evidence.Single(x => x.ActionKind == "Consumption").Qty);
        Assert.IsTrue(f.Evidence.All(x => x.DocumentId.HasValue && x.DocumentLineId.HasValue && x.LedgerId.HasValue));
        Assert.IsTrue((await f.Service.PostAsync(command, default)).AlreadyApplied);
        Assert.AreEqual(1, f.Events.Count(x => x == "inbound"));
        Assert.AreEqual(1, f.Events.Count(x => x == "outbound"));
    }

    /// <summary>库存编排各阶段异常都请求回滚而不提交；替身不用于证明 SQL 数据回滚。</summary>
    [TestMethod]
    [DataRow("prepare")]
    [DataRow("inbound")]
    [DataRow("outbound")]
    [DataRow("mark")]
    [DataRow("append")]
    [DataRow("log")]
    public async Task InventoryFailureNeverCommits(string stage)
    {
        var f = new Fixture(inventory: true) { FailureStage = stage };
        var preview = await f.Service.PreviewAsync(f.Head.Uid, 1, f.Date, default);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Service.PostAsync(
            new(f.Head.Uid, Guid.NewGuid(), 1, f.Date, preview.Token!), default));
        Assert.AreEqual(0, f.Commits);
        Assert.AreEqual(1, f.Rollbacks);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>整单反向先恢复出库、再撤销收货，保留原证据并追加新流水关联。</summary>
    [TestMethod]
    public async Task InventoryReverseUsesOriginalDocumentsInSafeOrder()
    {
        var original = new PackingListReversePreviewBuilderTests.Fixture().Facts;
        var f = new Fixture(original: original);
        var preview = await f.Service.ReversePreviewAsync(f.Head.Uid, f.Head.Version, f.Date, "更正", default);
        Assert.AreEqual(0, preview.Blockers.Count);
        var command = new PackingListReverseRequest(f.Head.Uid, Guid.NewGuid(), f.Head.Version, f.Date, "更正", preview.Token!);
        var result = await f.Service.ReverseAsync(command, default);
        Assert.IsTrue(f.Events.IndexOf("reverse-outbound") < f.Events.IndexOf("reverse-inbound"));
        Assert.AreEqual(2, result.Documents.Count);
        Assert.AreEqual(4, f.Evidence.Count);
        Assert.IsTrue(f.Evidence.Skip(2).All(x => x.OriginalExecutionLineId.HasValue));
        Assert.AreEqual(4, f.Evidence.Select(x => x.LedgerId).Distinct().Count());
        Assert.AreEqual(PackingListPostingStatusEnum.Reversed, f.Head.PostingStatus);
        Assert.IsTrue((await f.Service.ReverseAsync(command, default)).AlreadyApplied);
        Assert.AreEqual(1, f.Events.Count(x => x == "reverse-outbound"));
    }

    /// <summary>任一反向库存端口失败必须回滚，不能提交箱单反向状态。</summary>
    [TestMethod]
    [DataRow("reverse-outbound")]
    [DataRow("reverse-inbound")]
    public async Task InventoryReverseFailureNeverCommits(string stage)
    {
        var f = new Fixture(original: new PackingListReversePreviewBuilderTests.Fixture().Facts) { FailureStage = stage };
        var preview = await f.Service.ReversePreviewAsync(f.Head.Uid, f.Head.Version, f.Date, "更正", default);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Service.ReverseAsync(
            new(f.Head.Uid, Guid.NewGuid(), f.Head.Version, f.Date, "更正", preview.Token!), default));
        Assert.AreEqual(0, f.Commits);
        Assert.AreEqual(1, f.Rollbacks);
        Assert.AreEqual(PackingListPostingStatusEnum.Posted, f.Head.PostingStatus);
    }

    /// <summary>即使内部替身忽略取消，外层也必须在提交前检查令牌并回滚。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationAfterWritesNeverCommits(bool reverse)
    {
        using var cancellation = new CancellationTokenSource();
        var f = reverse ? new Fixture(original: new PackingListReversePreviewBuilderTests.Fixture().Facts) : new Fixture(inventory: true);
        f.CancelAfterLog = cancellation;
        if (reverse)
        {
            var preview = await f.Service.ReversePreviewAsync(f.Head.Uid, f.Head.Version, f.Date, "更正", default);
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Service.ReverseAsync(
                new(f.Head.Uid, Guid.NewGuid(), f.Head.Version, f.Date, "更正", preview.Token!), cancellation.Token));
        }
        else
        {
            var preview = await f.Service.PreviewAsync(f.Head.Uid, f.Head.Version, f.Date, default);
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Service.PostAsync(
                new(f.Head.Uid, Guid.NewGuid(), f.Head.Version, f.Date, preview.Token!), cancellation.Token));
        }
        Assert.Contains("append", f.Events);
        Assert.AreEqual(0, f.Commits);
        Assert.AreEqual(1, f.Rollbacks);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>事务外探测后交付净额变化，锁后必须重新计算并拒绝旧令牌。</summary>
    [TestMethod]
    public async Task RechecksDeliveryAfterCandidateProbe()
    {
        var f = new Fixture();
        var preview = await f.Service.PreviewAsync(f.Head.Uid, 1, f.Date, default);
        f.ChangeDeliveryAfterLock = true;
        var error = await Assert.ThrowsExactlyAsync<PackingListPostingFailure>(() =>
            f.Service.PostAsync(new(f.Head.Uid, Guid.NewGuid(), 1, f.Date, preview.Token!), default));
        Assert.AreEqual("PREVIEW_STALE", error.Code);
        Assert.AreEqual(0, f.Executions.Count);
        Assert.AreEqual(0, f.Commits);
        Assert.AreEqual(1, f.Rollbacks);
    }

    /// <summary>反向探测后证据缺失必须回滚，不能继续更新箱单状态。</summary>
    [TestMethod]
    public async Task RechecksReverseEvidenceAfterCandidateProbe()
    {
        var f = new Fixture();
        var preview = await f.Service.PreviewAsync(f.Head.Uid, 1, f.Date, default);
        await f.Service.PostAsync(new(f.Head.Uid, Guid.NewGuid(), 1, f.Date, preview.Token!), default);
        var reverse = await f.Service.ReversePreviewAsync(f.Head.Uid, f.Head.Version, f.Date, "录单错误", default);
        f.HideReverseEvidenceAfterLock = true;
        var error = await Assert.ThrowsExactlyAsync<PackingListPostingFailure>(() => f.Service.ReverseAsync(
            new(f.Head.Uid, Guid.NewGuid(), f.Head.Version, f.Date, "录单错误", reverse.Token!), default));
        Assert.AreEqual("EXECUTION_INVALID", error.Code);
        Assert.AreEqual(1, f.Executions.Count);
        Assert.AreEqual(PackingListPostingStatusEnum.Posted, f.Head.PostingStatus);
        Assert.AreEqual(1, f.Commits);
        Assert.AreEqual(1, f.Rollbacks);
    }

    /// <summary>未预期的接口调用直接失败，避免默认成功掩盖依赖错误。</summary>
    public class Proxy : DispatchProxy
    {
        public Func<string, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!.Name, args ?? []);
        public static T Make<T>(Func<string, object?[], object?> handler) where T : class
        { var value = Create<T, Proxy>(); ((Proxy)(object)value).Handler = handler; return value; }
    }

    /// <summary>最小售后交付环境，无数据库连接，记录事务控制调用。</summary>
    private sealed class Fixture
    {
        public DateTime Date { get; } = new(2026, 9, 17);
        public PackingListHead Head { get; } = new() { PackingListNo = "PL1", CustomerId = Guid.NewGuid() };
        public List<PackingListPostingExecution> Executions { get; } = [];
        public List<PackingListPostingExecutionLine> Evidence { get; } = [];
        public List<string> Events { get; } = [];
        public DbSession Session { get; }
        public PackingListPostingCoordinator Service { get; }
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public bool FailLog { get; set; }
        public bool RejectCurrentReads { get; set; }
        public string? FailureStage { get; set; }
        public CancellationTokenSource? CancelAfterLog { get; set; }
        public bool ChangeDeliveryAfterLock { get; set; }
        public bool HideReverseEvidenceAfterLock { get; set; }
        public Fixture(bool inventory = false, PackingListReverseFacts? original = null)
        {
            if (original != null) { Head = original.Head!; Executions.AddRange(original.Executions); Evidence.AddRange(original.Evidence); }
            var source = new GrossDemand { CustomerId = Head.CustomerId, SourceType = "CR", IsNonStandard = true, DemandQty = 10 };
            var line = original?.PackingLines.Single() ?? new PackingListLine { PackingListHeadId = Head.Uid, LineNumber = 1, GrossDemandId = source.Uid, IsNonStandardSnapshot = true, Qty = 5 };
            var data = new PackingListPostingFacts { Head = Head, Lines = [line], Sources = [source], CustomerExists = true };
            IReadOnlyList<InboundPurchasePostingSourceDto> capacity = [];
            if (inventory)
            {
                var product = new Product { Sku = "FINISHED", Unit = "PCS" };
                var component = new Product { Sku = "SUPPLIED", Unit = "PCS", MaterialType = MaterialTypesEnum.客供件 };
                var warehouse = new WarehouseSite { Code = "VIRTUAL", IsActive = true, SupplierId = Guid.NewGuid(), SiteType = WarehouseSiteTypeEnum.SupplierVirtual };
                source.SourceType = "SO"; source.IsNonStandard = false; source.ProductId = product.Uid;
                source.UnitSnapshot = "PCS"; source.DemandBasisFingerprint = "basis";
                line.ProductId = product.Uid; line.UnitSnapshot = "PCS"; line.IsNonStandardSnapshot = false;
                var root = new NetDemand { GrossDemandId = source.Uid, ProductId = product.Uid, GrossQty = 10,
                    SourceDemandBasisFingerprintSnapshot = "basis", DecompositionRevision = 1 };
                root.RootNetDemandId = root.Uid;
                var child = new NetDemand { GrossDemandId = source.Uid, ProductId = component.Uid, GrossQty = 4,
                    ParentNetDemandId = root.Uid, RootNetDemandId = root.Uid, DeliveryTargetNetDemandId = root.Uid,
                    DeliveryMode = DemandDeliveryModeEnum.ParentExecutionSupplier, MaterialTypeSnapshot = MaterialTypesEnum.客供件, DecompositionRevision = 1 };
                var purchase = new PurchaseOrderHead { SupplierId = warehouse.SupplierId!.Value };
                var purchaseLine = new PurchaseOrderLine { PurchaseOrderId = purchase.Uid, ProductId = product.Uid, Quantity = 10 };
                data.Demands = [root, child]; data.Products = [product, component]; data.Warehouses = [warehouse];
                data.PurchaseHeads = [purchase]; data.PurchaseLines = [purchaseLine];
                data.Links = [new DemandExecutionLink { NetDemandId = root.Uid, TargetType = "PO", TargetHeadId = purchase.Uid,
                    TargetLineId = purchaseLine.Uid, AllocatedQty = 10 }];
                data.Balances = [new InventoryBalance { WarehouseSiteId = warehouse.Uid, ProductId = component.Uid, UnitSnapshot = "PCS", OnHandQty = 2 }];
                capacity = [new InboundPurchasePostingSourceDto { SourceLineId = purchaseLine.Uid, PurchaseOrderId = purchase.Uid,
                    ProductId = product.Uid, Unit = "PCS", CurrentUnit = "PCS", IsReceivable = true, OriginalQty = 10 }];
            }
            var transaction = Proxy.Make<IDbTransaction>((m, _) => m switch
            { "Commit" => Count(true), "Rollback" => Count(false), "Dispose" => null, "get_IsolationLevel" => IsolationLevel.Serializable, _ => throw new NotSupportedException(m) });
            var connection = Proxy.Make<IDbConnection>((m, _) => m == "BeginTransaction" ? transaction : throw new NotSupportedException(m));
            Session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Session, connection);
            var packing = Proxy.Make<IPackingListRepository>((m, _) => m switch
            {
                "GetDetailAsync" when RejectCurrentReads => throw new AssertFailedException("重放不应读取当前详情。"),
                "GetDetailAsync" => Task.FromResult<PackingListDetailDto?>(new() { Header = new() { Uid = Head.Uid, Version = Head.Version }, Lines = [new() { Uid = line.Uid, GrossDemandId = line.GrossDemandId, Qty = line.Qty }] }),
                "LockCommandAsync" => Task.FromResult(0), "LockHeaderAsync" => Record("head", Task.FromResult<PackingListHead?>(Head)),
                "LockLinesAsync" => Task.FromResult<IReadOnlyList<PackingListLine>>([line]), _ => throw new NotSupportedException(m)
            });
            var facts = Proxy.Make<IPackingListPostingFactsRepository>((m, _) => m switch
            {
                "ReadAsync" when RejectCurrentReads => throw new AssertFailedException("重放不应读取当前来源库存。"),
                "ReadAsync" => ReadFacts(),
                "LockSourcesAsync" => Record("sources", Task.CompletedTask), _ => throw new NotSupportedException(m)
            });
            var executions = Proxy.Make<IPackingListExecutionRepository>((m, args) => m switch
            {
                "ReadReverseFactsAsync" when RejectCurrentReads => throw new AssertFailedException("重放不应读取当前反向事实。"),
                "ReadReverseFactsAsync" when original != null => Task.FromResult(original),
                "IsSchemaReadyAsync" => Task.FromResult(true), "FindCommandAsync" => Task.FromResult(Executions.SingleOrDefault(x => x.CommandId == (Guid)args[0]!)),
                "AppendAsync" => Append((PackingListPostingExecution)args[0]!, (IReadOnlyList<PackingListPostingExecutionLine>)args[1]!), "MarkAsync" => Mark((bool)args[2]!),
                "ReadReverseFactsAsync" => Task.FromResult(new PackingListReverseFacts { Head = Head, PackingLines = [line], Executions = Executions.ToArray(),
                    Evidence = HideReverseEvidenceAfterLock && Session.Transaction != null ? [] : Evidence.ToArray() }),
                _ => throw new NotSupportedException(m)
            });
            var logs = Proxy.Make<IBusinessOperationLogRepository>((m, _) => m switch
            { "GetByModuleCommandAsync" => Task.FromResult<BusinessOperationLog?>(null), "InsertAsync" => Record("log", Task.FromResult(FailLog ? 0 : 1)), _ => throw new NotSupportedException(m) });
            var userId = Guid.NewGuid();
            var user = Proxy.Make<ICurrentUser>((m, _) => m switch
            { "get_UserId" => userId, "get_UserName" => "tester", "get_IsAuthenticated" => true, _ => throw new NotSupportedException(m) });
            var permissions = Proxy.Make<IUserPermissionService>((m, _) => m == "HasPermissionAsync" ? Task.FromResult(true) : throw new NotSupportedException(m));
            Service = new(new ServiceBaseContext(Session, NullLoggerFactory.Instance), packing, facts, executions,
                inventory ? Proxy.Make<IInboundManagerRepository>((m, _) => m switch {
                    "ReadPostingSourcesAsync" => Task.FromResult(capacity), "HasUnprovenReceiptAsync" => Task.FromResult(false), _ => throw new NotSupportedException(m) }) : Forbidden<IInboundManagerRepository>(),
                inventory || original != null ? Proxy.Make<IAutomaticPostingScope>((m, _) => m == "PrepareAsync" ? Record("prepare", Task.CompletedTask) : throw new NotSupportedException(m)) : Forbidden<IAutomaticPostingScope>(),
                inventory || original != null ? Proxy.Make<IInboundPostingPort>((m, a) => m switch {
                    "CreateAndPostAsync" => Port("inbound", (AutomaticPostingRequest)a[0]!),
                    "ReverseAsync" => ReversePort("reverse-inbound", (AutomaticReverseRequest)a[0]!), _ => throw new NotSupportedException(m) }) : Forbidden<IInboundPostingPort>(),
                inventory || original != null ? Proxy.Make<IOutboundPostingPort>((m, a) => m switch {
                    "CreateAndPostAsync" => Port("outbound", (AutomaticPostingRequest)a[0]!),
                    "ReverseAsync" => ReversePort("reverse-outbound", (AutomaticReverseRequest)a[0]!), _ => throw new NotSupportedException(m) }) : Forbidden<IOutboundPostingPort>(), logs, user, permissions);
            Task<PackingListPostingFacts> ReadFacts()
            {
                data.Delivered = ChangeDeliveryAfterLock && Session.Transaction != null
                    ? new Dictionary<Guid, decimal> { [source.Uid] = 1 } : new Dictionary<Guid, decimal>();
                return Task.FromResult(data);
            }
        }
        private static T Forbidden<T>() where T : class => Proxy.Make<T>((m, _) => throw new AssertFailedException($"不应调用 {typeof(T).Name}.{m}"));
        private object? Count(bool commit) { Events.Add(commit ? "commit" : "rollback"); if (commit) Commits++; else Rollbacks++; return null; }
        private T Record<T>(string name, T value) { Events.Add(name); if (FailureStage == name) throw new InvalidOperationException("测试阶段失败：" + name); if (name == "log") CancelAfterLog?.Cancel(); return value; }
        private Task Mark(bool reverse) { Record("mark", 0); Head.PostingStatus = reverse ? PackingListPostingStatusEnum.Reversed : PackingListPostingStatusEnum.Posted; Head.Version++; return Task.CompletedTask; }
        private Task Append(PackingListPostingExecution execution, IReadOnlyList<PackingListPostingExecutionLine> lines) { Record("append", 0); Executions.Add(execution); Evidence.AddRange(lines); return Task.CompletedTask; }

        /// <summary>模拟严格端口返回，验证同一事务及准备、入库、出库顺序，不模拟 SQL 库存。</summary>
        private Task<AutomaticPostingResult> Port(string stage, AutomaticPostingRequest request)
        {
            Assert.IsNotNull(Session.Transaction);
            Assert.Contains("prepare", Events);
            if (stage == "outbound") Assert.Contains("inbound", Events);
            Record(stage, 0);
            return Task.FromResult(new AutomaticPostingResult { Documents = request.Groups.Select(g => new AutomaticDocumentResult {
                DocumentId = g.GroupId, DocumentNo = stage + g.GroupId, Version = 2, PostingStatus = 1,
                Lines = g.Lines.Select(l => new AutomaticLineResult(l.LineId, l.LineId, Guid.NewGuid(), l.BasisId)).ToList()
            }).ToList() });
        }

        /// <summary>仅按原证据返回新反向流水，用于验证协调器不向端口传入重新分配数量。</summary>
        private Task<AutomaticPostingResult> ReversePort(string stage, AutomaticReverseRequest request)
        {
            Assert.IsNotNull(Session.Transaction);
            Assert.Contains("prepare", Events);
            if (stage == "reverse-inbound") Assert.Contains("reverse-outbound", Events);
            Record(stage, 0);
            return Task.FromResult(new AutomaticPostingResult { Documents = request.Documents.Select(d => new AutomaticDocumentResult {
                DocumentId = d.DocumentId, Version = d.ExpectedVersion + 1, PostingStatus = 8,
                Lines = Evidence.Where(x => x.DocumentId == d.DocumentId).Select(x => new AutomaticLineResult(
                    x.DocumentLineId!.Value, x.DocumentLineId.Value, Guid.NewGuid())).ToList()
            }).ToList() });
        }
    }
}
