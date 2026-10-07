using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Query;
using SwiftExport.Infrastructure.Services.Auth;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.DemandPlanning.Sources.Query;

namespace SwiftExport.Application.Tests.Database;

/// <summary>固定故障测试库的真实Gross Trace场景；本轮只编译，主控启用门禁后串行执行，所有夹具事务回滚。</summary>
[DoNotParallelize]
public sealed partial class PostgreSqlGrossDemandTraceMigrationTests
{
    /// <summary>真实投影保持当前/历史/软删物料和完整采购行收货、最新反馈、共享关系；同事务自写可读且返回后仍可查询。</summary>
    [TestMethod, TestCategory("PostgreSqlGrossDemandTraceIntegration")]
    public Task RealPg_ProjectionHistoryPurchaseFeedbackAndShared() => WithRollback(async f =>
    {
        await f.Basic(); await f.Purchase(); await f.Receipt(7m);
        var old = f.Net(1, 1); old.LifecycleStatus = (NetDemandLifecycleStatusEnum)8; await f.Insert(old);
        var deleted = f.Net(1, 2); deleted.IsDeleted = true; await f.Insert(deleted);
        var otherGross = f.GrossRow(); await f.Insert(otherGross); var otherNet = f.Net(1, 1, otherGross.Uid); await f.Insert(otherNet);
        // 当前CHECK仅允许PO；以合法共享关系验证，不伪造非PO或禁用约束。源无额外TargetType条件由离线调用保留检查。
        var shared = f.Link(otherNet.Uid); await f.Insert(shared);
        await f.Insert(new SupplierProgressReport { Uid = Guid.NewGuid(), PurchaseOrderLineId = f.PurchaseLine.Uid,
            Sequence = 1, ReadyQty = 2m, SupplierReportedDate = Day, CommandId = Guid.NewGuid() });
        await f.Insert(new SupplierProgressReport { Uid = Guid.NewGuid(), PurchaseOrderLineId = f.PurchaseLine.Uid,
            Sequence = 2, ReadyQty = 9m, ExpectedReadyDate = Day.AddDays(1), SupplierReportedDate = Day, CommandId = Guid.NewGuid() });
        var result = await f.Repository.GetTraceAsync(f.Gross.Uid);
        Assert.IsNotNull(result); Assert.HasCount(3, result.Materials);
        Assert.AreEqual(6m, result.Materials.Single(row => row.NetDemandId == f.Material.Uid).ActivePurchaseQty);
        Assert.IsNull(result.Materials.Single(row => row.NetDemandId == old.Uid).ActivePurchaseQty);
        Assert.AreEqual("已删除物料行", result.Materials.Single(row => row.NetDemandId == deleted.Uid).ReviewReason);
        var purchase = result.Purchases.Single(); Assert.AreEqual(f.PurchaseLine.Uid, purchase.PurchaseLineId);
        Assert.AreEqual(12m, purchase.PurchaseLineQty); Assert.AreEqual(6m, purchase.LinkedQty);
        Assert.AreEqual(9m, purchase.PurchaseLineReadyQty); Assert.AreEqual(7m, purchase.PurchaseLineReceivedQty);
        Assert.IsTrue(purchase.ExpectedReadyDate == Day.AddDays(1)); Assert.IsTrue(purchase.IsSharedPurchaseLine);
        Assert.IsTrue(purchase.HasPurchaseTarget); Assert.AreEqual("", purchase.ReviewReason);
        Assert.AreSame(f.Transaction, f.Session.Transaction); Assert.AreEqual(1, await f.Scalar("SELECT 1"));
    });

    /// <summary>NULL最新反馈不被旧报告补值；没有正式收货时返回零，而非未知。</summary>
    [TestMethod, TestCategory("PostgreSqlGrossDemandTraceIntegration")]
    public Task RealPg_NullLatestReportAndZeroReceipts() => WithRollback(async f =>
    {
        await f.Basic(); await f.Purchase();
        await f.Insert(new SupplierProgressReport { Uid = Guid.NewGuid(), PurchaseOrderLineId = f.PurchaseLine.Uid,
            Sequence = 1, ReadyQty = 5m, SupplierReportedDate = Day, CommandId = Guid.NewGuid() });
        await f.Insert(new SupplierProgressReport { Uid = Guid.NewGuid(), PurchaseOrderLineId = f.PurchaseLine.Uid,
            Sequence = 2, ReadyQty = null, ExpectedReadyDate = null, SupplierReportedDate = Day, CommandId = Guid.NewGuid() });
        var purchase = (await f.Repository.GetTraceAsync(f.Gross.Uid))!.Purchases.Single();
        Assert.IsNull(purchase.PurchaseLineReadyQty); Assert.IsNull(purchase.ExpectedReadyDate); Assert.AreEqual(0m, purchase.PurchaseLineReceivedQty);
    });

    /// <summary>缺目标、单位错、客户错、版本/指纹旧及历史释放均由原SQL资格和理由解释。</summary>
    [TestMethod, TestCategory("PostgreSqlGrossDemandTraceIntegration")]
    [DataRow("missing")][DataRow("deleted-target")][DataRow("cancelled-target")][DataRow("unit")]
    [DataRow("customer")][DataRow("fingerprint")][DataRow("version")][DataRow("source-version")][DataRow("history")]
    public Task RealPg_PurchaseReasonsAndUnknownQuantities(string mode) => WithRollback(async f =>
    {
        await f.Basic();
        if (mode == "missing") f.CurrentLink.TargetLineId = Guid.NewGuid();
        if (mode == "history") { f.CurrentLink.ReleasedQty = f.CurrentLink.AllocatedQty; f.CurrentLink.LinkStatus = (DemandExecutionLinkStatusEnum)8; }
        await f.Purchase();
        switch (mode)
        {
            case "missing": break;
            case "deleted-target": await f.Execute("UPDATE public.purchase_order_line SET is_deleted=TRUE WHERE uid=@Uid", new { f.PurchaseLine.Uid }); break;
            case "cancelled-target": await f.Execute("UPDATE public.purchase_order_head SET lifecycle_status=9 WHERE uid=@Uid", new { f.Order.Uid }); break;
            case "unit": await f.Execute("UPDATE public.purchase_order_line SET unit_snapshot='KG' WHERE uid=@Uid", new { f.PurchaseLine.Uid }); break;
            case "customer": await f.Execute("UPDATE public.purchase_order_head SET customer_id=@Other WHERE uid=@Uid", new { Other = Guid.NewGuid(), f.Order.Uid }); break;
            case "fingerprint": await f.Execute("UPDATE public.gross_demand SET demand_basis_fingerprint=NULL WHERE uid=@Uid", new { f.Gross.Uid }); break;
            case "version": await f.Execute("UPDATE public.net_demand SET source_demand_version_snapshot=2 WHERE uid=@Uid", new { f.Material.Uid }); break;
            case "source-version": await f.Execute("UPDATE public.gross_demand SET source_type='SO',source_version=NULL WHERE uid=@Uid", new { f.Gross.Uid }); break;
            case "history": break;
        }
        var result = await f.Repository.GetTraceAsync(f.Gross.Uid); var material = result!.Materials.Single(); var purchase = result.Purchases.Single();
        if (mode == "history") { Assert.AreEqual(0m, material.ActivePurchaseQty); Assert.AreEqual(0m, purchase.LinkedQty); Assert.AreEqual("历史采购关联", purchase.ReviewReason); }
        else { Assert.IsNull(material.ActivePurchaseQty); Assert.IsFalse(string.IsNullOrEmpty(material.ReviewReason)); Assert.IsFalse(string.IsNullOrEmpty(purchase.ReviewReason)); }
        if (mode is "missing" or "deleted-target") { Assert.IsFalse(purchase.HasPurchaseTarget); Assert.AreEqual(0m, purchase.PurchaseLineQty); }
    });

    /// <summary>post4与reverse3显示两行但净量仅4；来源指纹/版本理由不阻断已有合法交付。</summary>
    [TestMethod, TestCategory("PostgreSqlGrossDemandTraceIntegration")]
    [DataRow("valid")][DataRow("fingerprint")][DataRow("version")]
    public Task RealPg_PostedAndReversedDeliveryKeepSourceReasonIndependent(string mode) => WithRollback(async f =>
    {
        await f.Basic(); await f.Packing(false, 4m); await f.Packing(true, 3m);
        if (mode == "fingerprint") await f.Execute("UPDATE public.gross_demand SET demand_basis_fingerprint=NULL WHERE uid=@Uid", new { f.Gross.Uid });
        if (mode == "version")
        {
            await f.Execute("UPDATE public.gross_demand SET source_type='SO',source_version=NULL WHERE uid=@Uid", new { f.Gross.Uid });
            await f.Execute("UPDATE public.packing_list_line SET source_type_snapshot='SO' WHERE gross_demand_id=@Uid", new { f.Gross.Uid });
        }
        var result = await f.Repository.GetTraceAsync(f.Gross.Uid);
        Assert.AreEqual(4m, result!.DeliveredQty); Assert.HasCount(2, result.Deliveries);
        Assert.AreEqual(3m, result.Deliveries.Single(row => !row.CountsTowardDelivery).Qty);
        Assert.AreEqual(mode == "valid" ? "" : "来源身份、版本或需求指纹需核实", result.ReviewReason);
    });

    /// <summary>装箱客户/单位/来源快照、缺日期、孤儿与软删过账独立阻断确定交付量，合法反向不重复减量。</summary>
    [TestMethod, TestCategory("PostgreSqlGrossDemandTraceIntegration")]
    [DataRow("customer")][DataRow("unit")][DataRow("sku")][DataRow("source")][DataRow("date")]
    [DataRow("orphan")][DataRow("deleted-line")][DataRow("deleted-head")][DataRow("raw-unit")]
    public Task RealPg_InvalidPackingEvidenceStaysUnknown(string mode) => WithRollback(async f =>
    {
        await f.Basic(); var (head, line) = await f.Packing(false, 4m);
        switch (mode)
        {
            case "customer": await f.Execute("UPDATE public.packing_list_head SET customer_id=@Other WHERE uid=@Uid", new { Other = Guid.NewGuid(), head.Uid }); break;
            case "unit": await f.Execute("UPDATE public.packing_list_line SET unit_snapshot='KG' WHERE uid=@Uid", new { line.Uid }); break;
            case "sku": await f.Execute("UPDATE public.packing_list_line SET sku_snapshot='BAD' WHERE uid=@Uid", new { line.Uid }); break;
            case "source": await f.Execute("UPDATE public.packing_list_line SET source_document_no_snapshot='BAD' WHERE uid=@Uid", new { line.Uid }); break;
            case "date": await f.Execute("UPDATE public.packing_list_head SET posted_date=NULL WHERE uid=@Uid", new { head.Uid }); break;
            case "orphan": await f.Execute("UPDATE public.packing_list_line SET packing_list_head_id=@Other WHERE uid=@Uid", new { Other = Guid.NewGuid(), line.Uid }); break;
            case "deleted-line": await f.Execute("UPDATE public.packing_list_line SET is_deleted=TRUE WHERE uid=@Uid", new { line.Uid }); break;
            case "deleted-head": await f.Execute("UPDATE public.packing_list_head SET is_deleted=TRUE WHERE uid=@Uid", new { head.Uid }); break;
            // 展示按ASCII双边修剪认为一致，尾部原始比较保留前导空格差异而阻断。
            case "raw-unit": await f.Execute("UPDATE public.packing_list_line SET unit_snapshot=' PCS' WHERE uid=@Uid", new { line.Uid }); break;
        }
        var result = await f.Repository.GetTraceAsync(f.Gross.Uid); Assert.IsNull(result!.DeliveredQty);
        StringAssert.Contains(result.ReviewReason, "装箱历史");
        if (mode is "orphan" or "deleted-line" or "deleted-head") Assert.HasCount(0, result.Deliveries);
        if (mode == "raw-unit") Assert.AreEqual("", result.Deliveries.Single().ReviewReason);
    });

    /// <summary>无关坏历史不扩大scope；相关头的其他坏行使整个头不可信，已完整释放的历史Link仍参与定位。</summary>
    [TestMethod, TestCategory("PostgreSqlGrossDemandTraceIntegration")]
    [DataRow("related")][DataRow("history-link")][DataRow("deleted-scope-line")][DataRow("other-line")][DataRow("unrelated")]
    public Task RealPg_ScopedGuardChecksWholeHeadBeforeHeader(string mode) => WithRollback(async f =>
    {
        await f.Basic();
        if (mode == "history-link") { f.CurrentLink.ReleasedQty = f.CurrentLink.AllocatedQty; f.CurrentLink.LinkStatus = (DemandExecutionLinkStatusEnum)8; }
        await f.Purchase();
        var (head, line) = await f.Receipt(7m, mode == "unrelated" ? Guid.NewGuid() : f.PurchaseLine.Uid);
        if (mode == "other-line")
        {
            var other = new InboundDocumentLine { Uid = Guid.NewGuid(), InboundDocumentId = head.Uid, LineNumber = 2,
                ProductId = Guid.NewGuid(), SkuSnapshot = "OTHER", UnitSnapshot = "PCS", Quantity = 1m,
                SourceLineId = Guid.NewGuid(), OriginalLineId = Guid.NewGuid() }; await f.Insert(other);
        }
        else await f.Execute("UPDATE public.inbound_document SET posting_status=8 WHERE uid=@Uid", new { head.Uid });
        if (mode == "deleted-scope-line") await f.Execute("UPDATE public.inbound_document_line SET is_deleted=TRUE WHERE uid=@Uid", new { line.Uid });
        if (mode == "unrelated") Assert.IsNotNull(await f.Repository.GetTraceAsync(f.Gross.Uid));
        else
        {
            // 即使头已删除也先验证历史，不先返回未找到。
            await f.Execute("UPDATE public.gross_demand SET is_deleted=TRUE WHERE uid=@Uid", new { f.Gross.Uid });
            var service = new GrossDemandQueryService(new ServiceBaseContext(f.Session, NullLoggerFactory.Instance), new TraceAdapter(f.Repository));
            var result = await service.GetTraceAsync(f.Gross.Uid); Assert.IsFalse(result.Success);
            Assert.AreEqual("正式入库反向历史需核实，收货量未知。", result.Message);
        }
        Assert.AreEqual(1, await f.Scalar("SELECT 1"));
    });

    /// <summary>未找到/软删头返回NULL，同事务还能查询；合法空集合给出确定零，没有临时资源残留。</summary>
    [TestMethod, TestCategory("PostgreSqlGrossDemandTraceIntegration")]
    [DataRow("missing")][DataRow("deleted")][DataRow("empty")]
    public Task RealPg_EmptyHeaderOrCollectionsKeepTransactionUsable(string mode) => WithRollback(async f =>
    {
        if (mode == "empty") { await f.Insert(f.Customer); await f.Insert(f.Gross); }
        else if (mode == "deleted") await f.Basic();
        if (mode == "deleted") await f.Execute("UPDATE public.gross_demand SET is_deleted=TRUE WHERE uid=@Uid", new { f.Gross.Uid });
        var result = await f.Repository.GetTraceAsync(f.Gross.Uid);
        if (mode == "empty")
        { Assert.IsNotNull(result); Assert.AreEqual(0m, result.DeliveredQty); Assert.HasCount(0, result.Materials); Assert.HasCount(0, result.Purchases); Assert.HasCount(0, result.Deliveries); }
        else Assert.IsNull(result);
        Assert.AreSame(f.Transaction, f.Session.Transaction); Assert.AreEqual(1, await f.Scalar("SELECT 1"));
        Assert.AreEqual(0, await f.Scalar("SELECT COUNT(*)::integer FROM pg_class WHERE relnamespace=pg_my_temp_schema() AND lower(relname) IN ('tracepurchaselines','materialreceiptcoverage')"));
    });

    /// <summary>真实无外层事务的缺失读取与预取消支持，仓储不开始事务或关闭既有连接。</summary>
    [TestMethod, TestCategory("PostgreSqlGrossDemandTraceIntegration")]
    public async Task RealPg_OptionalTransactionAndPreCancellation()
    {
        using var session = RealSession(); var context = RealContext(session); var repo = new PostgreSqlGrossDemandQueryRepository(context);
        Assert.IsNull(session.Transaction); Assert.IsNull(await repo.GetTraceAsync(Guid.NewGuid()));
        var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => repo.GetTraceAsync(Guid.NewGuid(), token));
        Assert.IsNull(session.Transaction); Assert.AreEqual(1, await session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
        var service = new GrossDemandQueryService(new ServiceBaseContext(session, NullLoggerFactory.Instance), new TraceAdapter(repo));
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetTraceAsync(Guid.NewGuid(), token));
    }

    /// <summary>每个读取阶段开始时由真实命令包装器等待只读PG语句，取消后由调用者回滚；生产没有测试回调。</summary>
    [TestMethod, TestCategory("PostgreSqlGrossDemandTraceIntegration")]
    [DataRow(0)][DataRow(1)][DataRow(2)][DataRow(3)][DataRow(4)][DataRow(5)][DataRow(6)]
    public async Task RealPg_StageWaitCancellationLeavesRecoveryToCaller(int stage)
    {
        using var factory = new StageFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString());
        using var session = new DbSession(factory); using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction; var fixture = new RealFixture(session, transaction); var rolledBack = false;
        using var cancellation = new CancellationTokenSource(); Task? read = null;
        try
        {
            await fixture.Basic(); factory.Connection.Stage = stage;
            read = fixture.Repository.GetTraceAsync(fixture.Gross.Uid, cancellation.Token);
            await factory.Connection.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => read);
            Assert.AreSame(transaction, session.Transaction); Assert.AreEqual(stage + 1, factory.Connection.TraceCommands);
            // 服务器取消可能使事务aborted，仓储不偷偷恢复；调用者显式回滚后复用连接。
            transaction.Rollback(); rolledBack = true; session.Transaction = null;
            Assert.AreEqual(1, await session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
        }
        finally
        {
            cancellation.Cancel();
            // 断言失败也先结束实际命令，再由调用者回滚，避免清理期间仍有驱动操作。
            try
            {
                if (read is not null && !read.IsCompleted)
                { try { await read; } catch (OperationCanceledException) { } }
            }
            finally { try { if (!rolledBack) transaction.Rollback(); } finally { session.Transaction = null; } }
        }
    }

    private static readonly DateTime Day = new(2026, 9, 2);

    /// <summary>严格复用固定故障库门禁，未配置则未运行，不提供默认连接或凭据。</summary>
    private static DbSession RealSession() => new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));

    /// <summary>共享会话和夹具身份，不修改公共依赖。</summary>
    private static RepositoryBaseContext RealContext(DbSession session)
    { var user = new TraceUser(); return new(session, NullLoggerFactory.Instance, new AuditStampService(user), user); }

    /// <summary>随机UID实体全部在一条ReadCommitted事务中写入/读取，并在finally回滚；不初始化结构或提交。</summary>
    private static async Task WithRollback(Func<RealFixture, Task> action)
    {
        using var session = RealSession(); using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try { await action(new(session, transaction)); Assert.AreSame(transaction, session.Transaction); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>只创建本例随机关系，用现有元数据INSERT保留指定UID及软删除，不经Add重置身份。</summary>
    private sealed class RealFixture
    {
        public DbSession Session { get; }
        public IDbTransaction Transaction { get; }
        public RepositoryBaseContext Context { get; }
        public PostgreSqlGrossDemandQueryRepository Repository { get; }
        public Customer Customer { get; } = new() { Uid = Guid.NewGuid(), CustomerCode = Code(), CustomerName = "Trace客户", DocRelateNo = Code() };
        public GrossDemand Gross { get; }
        public NetDemand Material { get; }
        public PurchaseOrderHead Order { get; }
        public PurchaseOrderLine PurchaseLine { get; }
        public DemandExecutionLink CurrentLink { get; }
        /// <summary>建立稳定随机关系，所有写入均由场景显式调用。</summary>
        public RealFixture(DbSession session, IDbTransaction transaction)
        {
            Session = session; Transaction = transaction; Context = RealContext(session); Repository = new(Context);
            Gross = GrossRow(); Material = Net(2, 1);
            Order = new() { Uid = Guid.NewGuid(), OriginKind = 1, CustomerId = Customer.Uid, OrderNo = Code(),
                SupplierId = Guid.NewGuid(), SupplierCode = Code(), ExecutionRequestId = Guid.NewGuid(), OrderDate = Day };
            PurchaseLine = new() { Uid = Guid.NewGuid(), PurchaseOrderId = Order.Uid, LineNumber = 1,
                OrderNo = Order.OrderNo, Sku = "MAT", UnitSnapshot = "PCS", Quantity = 12m };
            CurrentLink = Link(Material.Uid);
        }
        /// <summary>建立非标来源，避免夹具依赖已存在产品。</summary>
        public GrossDemand GrossRow()
        {
            var uid = Guid.NewGuid(); return new() { Uid = uid, SourceType = "MD", SourceUid = uid, SourceLineId = uid,
                SourceNo = Code(), CustomerId = Customer.Uid, IsNonStandard = true, SkuSnapshot = "SKU", UnitSnapshot = "PCS",
                DemandQty = 10m, DemandBasisFingerprint = new string('A', 64) };
        }
        /// <summary>历史修订和行号由场景指定，未分配物料保持相同来源快照。</summary>
        public NetDemand Net(int revision, int number, Guid? gross = null)
        {
            var uid = Guid.NewGuid(); return new() { Uid = uid, GrossDemandId = gross ?? Gross.Uid, RootNetDemandId = uid,
                SourceDemandVersionSnapshot = 1, SourceDemandBasisFingerprintSnapshot = new string('A', 64),
                DecompositionRevision = revision, LineNumber = number, SupplyTypeSnapshot = "采购", SkuSnapshot = "MAT",
                UnitSnapshot = "PCS", GrossQty = 6m, NetQty = 6m };
        }
        /// <summary>建立单条合法正承接，命令项与请求身份每条独立。</summary>
        public DemandExecutionLink Link(Guid material) => new() { Uid = Guid.NewGuid(), NetDemandId = material,
            ExecutionRequestId = Guid.NewGuid(), CommandItemId = Guid.NewGuid(), TargetType = "PO", TargetHeadId = Order.Uid,
            TargetLineId = PurchaseLine.Uid, TargetNo = Order.OrderNo, AllocatedQty = 8m, ReleasedQty = 2m };
        /// <summary>只新增本例基础关系。</summary>
        public async Task Basic() { await Insert(Customer); await Insert(Gross); await Insert(Material); }
        /// <summary>新增采购头行及当前承接。</summary>
        public async Task Purchase() { await Insert(Order); await Insert(PurchaseLine); await Insert(CurrentLink); }
        /// <summary>正式正量入库无需反向流水证明，来源可以按场景指向无关采购行。</summary>
        public async Task<(InboundDocument Head, InboundDocumentLine Line)> Receipt(decimal quantity, Guid? source = null)
        {
            var head = new InboundDocument { Uid = Guid.NewGuid(), DocumentNo = Code(), DocumentType = (InboundDocumentTypeEnum)10,
                PostingStatus = (InboundPostingStatusEnum)1, WarehouseSiteId = Guid.NewGuid(), ReceiptDate = Day };
            var line = new InboundDocumentLine { Uid = Guid.NewGuid(), InboundDocumentId = head.Uid, LineNumber = 1,
                ProductId = Guid.NewGuid(), SkuSnapshot = "MAT", UnitSnapshot = "PCS", Quantity = quantity, SourceLineId = source ?? PurchaseLine.Uid };
            await Insert(head); await Insert(line); return (head, line);
        }
        /// <summary>装箱仅保存非标交付事实；反向行仍展示正数但不贡献正式净交付。</summary>
        public async Task<(PackingListHead Head, PackingListLine Line)> Packing(bool reversed, decimal quantity)
        {
            var head = new PackingListHead { Uid = Guid.NewGuid(), PackingListNo = Code(), CustomerId = Customer.Uid,
                InvoiceDate = Day, PostingStatus = (PackingListPostingStatusEnum)(reversed ? 8 : 1), PostedDate = Day,
                ReversedDate = reversed ? Day.AddDays(1) : null, CreateCommandId = Guid.NewGuid() };
            var line = new PackingListLine { Uid = Guid.NewGuid(), PackingListHeadId = head.Uid, LineNumber = 1,
                GrossDemandId = Gross.Uid, IsNonStandardSnapshot = true, SourceTypeSnapshot = "MD",
                SourceDocumentNoSnapshot = Gross.SourceNo, SkuSnapshot = Gross.SkuSnapshot, UnitSnapshot = "PCS", Qty = quantity };
            await Insert(head); await Insert(line); return (head, line);
        }
        /// <summary>通过实体元数据绑定所有属性，审计打戳不重置UID/IsDeleted；不绕过CHECK或唯一约束。</summary>
        public async Task Insert<T>(T entity) where T : BaseEntity
        {
            Context.AuditStamp.StampForInsert(entity); var metadata = EntityMetadataCache.GetMetadata(typeof(T));
            var columns = string.Join(",", metadata.Columns.Select(column => PostgreSqlNames.Quote(column.ColumnName)));
            var parameters = string.Join(",", metadata.Columns.Select(column => "@" + column.ParameterName));
            Assert.AreEqual(1, await Session.Connection.ExecuteAsync(new CommandDefinition(
                $"INSERT INTO public.{PostgreSqlNames.Quote(metadata.TableName)} ({columns}) VALUES ({parameters})",
                PostgreSqlParameters.Entity(entity, metadata), Transaction)));
        }
        /// <summary>只更新本例登记的UID，仍在回滚事务内。</summary>
        public Task<int> Execute(string sql, object parameters) => Session.Connection.ExecuteAsync(new CommandDefinition(sql, parameters, Transaction));
        /// <summary>确认只读返回后调用者事务仍可继续。</summary>
        public Task<int> Scalar(string sql) => Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, transaction: Transaction));
    }

    /// <summary>业务唯一编码使用本例随机值，不依赖或污染已有数据。</summary>
    private static string Code() => "PGGT" + Guid.NewGuid().ToString("N").ToUpperInvariant();

    /// <summary>测试专用真实连接包装器只在命令执行边界加只读等待，不改业务SQL、参数或事务，不用于运行接线。</summary>
    private sealed class StageFactory : IDbConnectionFactory, IDisposable
    {
        public NpgsqlConnection Connection { get; }
        public IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance;
        /// <summary>仅由严格门禁提供连接串。</summary>
        public StageFactory(string connectionString) => Connection = new(connectionString);
        /// <summary>返回既有包装连接，真实连接由DbSession打开。</summary>
        public IDbConnection CreateConnection() => Connection;
        /// <summary>释放包装器的真实连接。</summary>
        public void Dispose() => Connection.Dispose();

        /// <summary>类型名使Dapper识别Npgsql数组能力；所有实际操作仍委托真实驱动。</summary>
        public sealed class NpgsqlConnection(string connectionString) : DbConnection
        {
            private readonly Npgsql.NpgsqlConnection inner = new(connectionString);
            public int? Stage { get; set; }
            public int TraceCommands { get; private set; }
            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            [AllowNull] public override string ConnectionString { get => inner.ConnectionString; set => inner.ConnectionString = value; }
            public override string Database => inner.Database;
            public override string DataSource => inner.DataSource;
            public override string ServerVersion => inner.ServerVersion;
            public override ConnectionState State => inner.State;
            /// <summary>委托打开，不另建连接。</summary>
            public override void Open() => inner.Open();
            /// <summary>仅生命周期关闭。</summary>
            public override void Close() => inner.Close();
            /// <summary>禁止测试切库。</summary>
            public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
            /// <summary>返回调用者拥有的真实事务。</summary>
            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => inner.BeginTransaction(isolationLevel);
            /// <summary>仅包装真实命令的执行边界。</summary>
            protected override DbCommand CreateDbCommand() => new StageCommand(this, inner.CreateCommand());
            /// <summary>测试阶段辅助语句是真实只读等待，取消传播驱动异常；不模拟数据库成功或业务结果。</summary>
            private async Task Before(DbTransaction? transaction, CancellationToken token)
            {
                if (!Stage.HasValue) return;
                if (TraceCommands++ != Stage.Value) return;
                Stage = null;
                using var wait = new Npgsql.NpgsqlCommand("SELECT pg_sleep(60)", inner, (Npgsql.NpgsqlTransaction?)transaction);
                var pending = wait.ExecuteScalarAsync(token);
                Entered.TrySetResult();
                await pending;
            }
            /// <summary>只释放真实连接，保留调用者事务恢复策略。</summary>
            protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }

            /// <summary>真实参数/SQL/事务完全委托，只在异步执行前施加测试只读屏障。</summary>
            private sealed class StageCommand(NpgsqlConnection owner, DbCommand innerCommand) : DbCommand
            {
                [AllowNull] public override string CommandText { get => innerCommand.CommandText; set => innerCommand.CommandText = value; }
                public override int CommandTimeout { get => innerCommand.CommandTimeout; set => innerCommand.CommandTimeout = value; }
                public override CommandType CommandType { get => innerCommand.CommandType; set => innerCommand.CommandType = value; }
                public override bool DesignTimeVisible { get => innerCommand.DesignTimeVisible; set => innerCommand.DesignTimeVisible = value; }
                public override UpdateRowSource UpdatedRowSource { get => innerCommand.UpdatedRowSource; set => innerCommand.UpdatedRowSource = value; }
                protected override DbConnection? DbConnection { get => owner; set { if (value != owner) throw new InvalidOperationException("测试不能切换连接"); } }
                protected override DbTransaction? DbTransaction { get => innerCommand.Transaction; set => innerCommand.Transaction = value; }
                protected override DbParameterCollection DbParameterCollection => innerCommand.Parameters;
                protected override DbParameter CreateDbParameter() => innerCommand.CreateParameter();
                /// <summary>取消交给真实命令。</summary>
                public override void Cancel() => innerCommand.Cancel();
                /// <summary>准备交给真实命令。</summary>
                public override void Prepare() => innerCommand.Prepare();
                /// <summary>夹具写入交给真实命令。</summary>
                public override int ExecuteNonQuery() => innerCommand.ExecuteNonQuery();
                /// <summary>标量交给真实命令。</summary>
                public override object? ExecuteScalar() => innerCommand.ExecuteScalar();
                /// <summary>同步读交给真实命令。</summary>
                protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => innerCommand.ExecuteReader(behavior);
                /// <summary>异步夹具写入不增加Trace阶段。</summary>
                public override Task<int> ExecuteNonQueryAsync(CancellationToken token) => innerCommand.ExecuteNonQueryAsync(token);
                /// <summary>Guard和尾部计数使用真实驱动与相同令牌。</summary>
                public override async Task<object?> ExecuteScalarAsync(CancellationToken token)
                { await owner.Before(DbTransaction, token); return await innerCommand.ExecuteScalarAsync(token); }
                /// <summary>集合读取使用真实驱动，不重写业务SQL。</summary>
                protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
                { await owner.Before(DbTransaction, token); return await innerCommand.ExecuteReaderAsync(behavior, token); }
                /// <summary>只释放当前命令。</summary>
                protected override void Dispose(bool disposing) { if (disposing) innerCommand.Dispose(); base.Dispose(disposing); }
            }
        }
    }
}
