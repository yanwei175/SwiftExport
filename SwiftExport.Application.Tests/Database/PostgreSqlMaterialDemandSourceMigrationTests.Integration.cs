using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Sources;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>供主控串行执行的真实 PG 物料来源验收；固定故障库门禁、每例随机身份单事务回滚，不执行 DDL 或清理既有数据。</summary>
[TestClass]
[DoNotParallelize]
[TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlMaterialDemandSourceMigrationIntegrationTests
{
    /// <summary>同 Uid 不同类型和同编号不同 Uid 不混组，超过 200 行完整返回；空来源和两类分组均调用真实仓储。</summary>
    [TestMethod]
    public async Task SourceGroupsAndRows_KeepCompositeIdentityAndEntireSource()
    {
        using var f = new Fixture();
        var source = Guid.NewGuid();
        var expected = new List<Guid>();
        for (var i = 0; i < 205; i++)
        {
            var gross = f.Gross(source, "SO", i + 1);
            gross.Version = 7; gross.SourceVersion = 3; gross.ImagePathSnapshot = "来源图";
            gross.RequiredDate = i == 204 ? null : new DateTime(2026, 10, 5);
            await f.InsertAsync(gross); expected.Add(gross.Uid);
        }
        var collision = f.Gross(source, "MD", 1);
        await f.InsertAsync(collision);
        var other = f.Gross(Guid.NewGuid(), "SO", 1);
        other.SourceNo = collision.SourceNo;
        await f.InsertAsync(other);
        var groups = (await f.Repository.GetSourceDemandCandidateGroupsAsync()).Where(g => g.SourceUid == source).ToList();
        Assert.HasCount(2, groups);
        Assert.AreEqual(205, groups.Single(g => g.SourceType == "SO").CandidateCount);
        Assert.AreEqual(1, groups.Single(g => g.SourceType == "MD").CandidateCount);
        var rows = await f.Repository.GetSourceDemandCandidatesBySourceAsync("so ", source);
        Assert.HasCount(205, rows);
        CollectionAssert.AreEquivalent(expected, rows.Select(r => r.GrossDemandId).ToList());
        Assert.AreEqual(expected[^1], rows[^1].GrossDemandId, "空日期应最后。");
        Assert.IsTrue(rows.All(r => r.SourceDemandVersion == 7 && r.SourceVersion == 3 && r.ImagePath == "来源图"));
        Assert.AreEqual(collision.Uid, (await f.Repository.GetSourceDemandCandidatesBySourceAsync("MD", source)).Single().GrossDemandId);
        Assert.HasCount(0, await f.Repository.GetSourceDemandCandidatesBySourceAsync("SO", Guid.NewGuid()));
        Assert.HasCount(0, await f.Repository.GetPurchaseCandidatesAsync(new() { SourceType = "SO", SourceUid = source }));
        Assert.IsFalse((await f.Repository.GetPurchaseCandidateSourceGroupsAsync()).Any(g => g.SourceUid == source));
    }

    /// <summary>NOT EXISTS 只排有效未删除物料；历史、删除物料不阻挡，来源关闭或删除仍不能建立。</summary>
    [TestMethod]
    [DataRow("active", false)] [DataRow("history", true)] [DataRow("deleted", true)]
    [DataRow("closed-source", false)] [DataRow("deleted-source", false)]
    public async Task SourceEligibility_PreservesActiveBoundary(string branch, bool included)
    {
        using var f = new Fixture();
        var gross = f.Gross(Guid.NewGuid(), "MD", 1);
        if (branch == "closed-source") gross.LifecycleStatus = GrossDemandLifecycleStatusEnum.Closed;
        if (branch == "deleted-source") gross.IsDeleted = true;
        await f.InsertAsync(gross);
        var net = f.Net(gross, 1);
        if (branch is "history" or "closed-source" or "deleted-source") net.LifecycleStatus = NetDemandLifecycleStatusEnum.RolledBack;
        if (branch == "deleted") net.IsDeleted = true;
        await f.InsertAsync(net);
        var rows = await f.Repository.GetSourceDemandCandidatesBySourceAsync("MD", gross.SourceUid);
        Assert.AreEqual(included ? 1 : 0, rows.Count);
        Assert.AreEqual(included, (await f.Repository.GetSourceDemandCandidateGroupsAsync()).Any(g => g.SourceUid == gross.SourceUid));
    }

    /// <summary>采购量固定 allocated-released；历史/失配目标不贡献，满额或超额不再成为候选；不为异常历史绕过当前 Link 约束。</summary>
    [TestMethod]
    [DataRow("normal", 3, true)]
    [DataRow("full", 10, false)] [DataRow("over", 18, false)]
    [DataRow("inactive-link", 0, true)]
    [DataRow("deleted-head", 0, true)] [DataRow("history-head", 0, true)]
    [DataRow("deleted-line", 0, true)] [DataRow("wrong-head", 0, true)]
    public async Task PurchaseEligibility_UsesOriginalContributionRules(string branch, int quantity, bool included)
    {
        using var f = new Fixture();
        var gross = f.Gross(Guid.NewGuid(), "MD", 1);
        await f.InsertAsync(gross);
        var net = f.Net(gross, 1); net.UnitSnapshot = null;
        await f.InsertAsync(net);
        var supplier = f.Supplier("S"); await f.InsertAsync(supplier);
        var head = f.Head(supplier, "PO1");
        if (branch == "deleted-head") head.IsDeleted = true;
        if (branch == "history-head") head.LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Voided;
        await f.InsertAsync(head);
        var line = f.Line(head, 1);
        if (branch == "deleted-line") line.IsDeleted = true;
        if (branch == "wrong-head") line.PurchaseOrderId = Guid.NewGuid();
        await f.InsertAsync(line);
        var link = f.Link(net, head, line, 5m, 2m);
        switch (branch)
        {
            case "full": link.AllocatedQty = 12; break;
            case "over": link.AllocatedQty = 20; break;
            case "inactive-link": link.LinkStatus = DemandExecutionLinkStatusEnum.Cancelled; link.ReleasedQty = link.AllocatedQty; break;
        }
        await f.InsertAsync(link);
        var rows = await f.Repository.GetPurchaseCandidatesAsync(new() { SourceType = "md ", SourceUid = gross.SourceUid });
        Assert.AreEqual(included ? 1 : 0, rows.Count);
        var groups = (await f.Repository.GetPurchaseCandidateSourceGroupsAsync()).Where(g => g.SourceUid == gross.SourceUid).ToList();
        Assert.AreEqual(included ? 1 : 0, groups.Count);
        if (included)
        {
            Assert.AreEqual((decimal)quantity, rows.Single().AssociatedPurchaseQty);
            Assert.AreEqual(10m - quantity, rows.Single().RemainingQty);
            Assert.IsNull(rows.Single().Unit, "候选不补单位，也不新增单位资格。");
            Assert.AreEqual(1, groups.Single().CandidateCount);
        }
    }

    /// <summary>当前目标约束拒绝旧异常 Link；只验证约束拒绝，不把这些未能建立的夹具声称为仓储历史查询通过。</summary>
    [TestMethod]
    [DataRow("deleted", "ck_demand_execution_link_not_deleted")]
    [DataRow("negative", "ck_demand_execution_link_quantity")]
    [DataRow("released-active", "ck_demand_execution_link_state")]
    [DataRow("wrong-type", "ck_demand_execution_link_target")]
    public async Task LegacyLinkFixtures_AreRejectedWithoutChangingConstraints(string branch, string constraint)
    {
        using var f = new Fixture();
        var gross = f.Gross(Guid.NewGuid(), "MD", 1); await f.InsertAsync(gross);
        var net = f.Net(gross, 1); await f.InsertAsync(net);
        var supplier = f.Supplier("S"); await f.InsertAsync(supplier);
        var head = f.Head(supplier, "PO1"); await f.InsertAsync(head);
        var line = f.Line(head, 1); await f.InsertAsync(line);
        var link = f.Link(net, head, line, 5m, 2m);
        switch (branch)
        {
            case "deleted": link.IsDeleted = true; break;
            case "negative": link.ReleasedQty = 7; break;
            case "released-active": link.ReleasedQty = 5; break;
            case "wrong-type": link.TargetType = "MO"; break;
        }
        var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => f.InsertAsync(link));
        Assert.AreEqual("23514", error.SqlState);
        Assert.AreEqual(constraint, error.ConstraintName);
    }

    /// <summary>活动产品/供应商不是候选筛选条件；只展示当前物料图和型号，单位仅修剪 ASCII 空格，价格可空。</summary>
    [TestMethod]
    [DataRow(null, null)] [DataRow(" KG ", "KG")] [DataRow("\t", "\t")]
    public async Task CurrentProductAndUnit_KeepCandidateProjection(string? storedUnit, string? expectedUnit)
    {
        using var f = new Fixture();
        var gross = f.Gross(Guid.NewGuid(), "MD", 1);
        gross.ImagePathSnapshot = "父来源图"; gross.LinkSkuSnapshot = "父来源型号"; gross.UnitSnapshot = "SET";
        await f.InsertAsync(gross);
        var product = new Product { Uid = Guid.NewGuid(), Sku = f.Prefix + "PRODUCT", SupplyType = SupplyTypeEnums.采购, Unit = "CURRENT", LinkSku = "当前物料型号", MainImage = "当前物料图", IsActive = false };
        await f.InsertAsync(product);
        var supplier = f.Supplier("S"); supplier.IsActive = false; await f.InsertAsync(supplier);
        var mapping = new SupplierProduct { Uid = Guid.NewGuid(), SupplierId = supplier.Uid, SupplierCode = supplier.SupplierCode, SkuId = product.Uid, ProductSku = product.Sku, SupplierSku = "供应商型号", UnitPrice = 2.5m, TaxPrice = null };
        await f.InsertAsync(mapping);
        var net = f.Net(gross, 1); net.ProductId = product.Uid; net.UnitSnapshot = storedUnit; net.SuggestedPurchaseSupplierId = supplier.Uid;
        await f.InsertAsync(net);
        var row = (await f.Repository.GetPurchaseCandidatesAsync(f.Query(gross))).Single();
        Assert.AreEqual("当前物料图", row.ImagePath); Assert.AreEqual("当前物料型号", row.LinkSku);
        Assert.AreEqual(expectedUnit, row.Unit); Assert.AreEqual("供应商型号", row.SuggestedSupplierSku);
        Assert.AreEqual(2.5m, row.SuggestedUnitPrice); Assert.IsNull(row.SuggestedTaxPrice);
        Assert.AreEqual(supplier.SupplierCode, row.SuggestedPurchaseSupplierCode);
        Assert.IsFalse(row.IsNonStandard); Assert.HasCount(0, row.ParentActualPurchaseSuppliers);
    }

    /// <summary>五项父目标关系及活动/删除边界均须满足；推荐不存在和已失效保留不同提示，停用未删供应商仍可展示。</summary>
    [TestMethod]
    [DataRow("valid", null)] [DataRow("parent", "送达目标关系无效")]
    [DataRow("gross", "送达目标关系无效")] [DataRow("root", "送达目标关系无效")]
    [DataRow("revision", "送达目标关系无效")] [DataRow("history", "送达目标关系无效")]
    [DataRow("deleted", "送达目标关系无效")] [DataRow("missing", "送达目标关系无效")]
    [DataRow("no-suggestion", "父件尚未设置推荐采购供应商")]
    [DataRow("deleted-supplier", "父件推荐采购供应商已失效")]
    public async Task DeliveryTarget_KeepsRelationshipAndHintBoundaries(string branch, string? hint)
    {
        using var f = new Fixture();
        var gross = f.Gross(Guid.NewGuid(), "MD", 1); await f.InsertAsync(gross);
        var supplier = f.Supplier("DELIVERY"); supplier.IsActive = false;
        if (branch == "deleted-supplier") supplier.IsDeleted = true;
        await f.InsertAsync(supplier);
        var parent = f.Net(gross, 1); parent.SupplyTypeSnapshot = "生产"; parent.SuggestedPurchaseSupplierId = supplier.Uid;
        var child = f.Net(gross, 2); child.RootNetDemandId = parent.Uid; child.ParentNetDemandId = parent.Uid;
        child.DeliveryTargetNetDemandId = parent.Uid; child.DeliveryMode = DemandDeliveryModeEnum.ParentExecutionSupplier;
        switch (branch)
        {
            case "parent": child.ParentNetDemandId = Guid.NewGuid(); break;
            case "gross": parent.GrossDemandId = Guid.NewGuid(); break;
            case "root": parent.RootNetDemandId = Guid.NewGuid(); break;
            case "revision": parent.DecompositionRevision = 2; break;
            case "history": parent.LifecycleStatus = NetDemandLifecycleStatusEnum.RolledBack; break;
            case "deleted": parent.IsDeleted = true; break;
            case "missing": child.DeliveryTargetNetDemandId = Guid.NewGuid(); break;
            case "no-suggestion": parent.SuggestedPurchaseSupplierId = null; break;
        }
        await f.InsertAsync(parent); await f.InsertAsync(child);
        var row = (await f.Repository.GetPurchaseCandidatesAsync(f.Query(gross))).Single();
        Assert.IsTrue(row.IsCustomerSupplied); Assert.AreEqual(hint, row.SuggestedDeliverySupplierHint);
        if (hint is null) Assert.AreEqual(supplier.SupplierCode, row.SuggestedDeliverySupplierCode);
        else Assert.IsNull(row.SuggestedDeliverySupplierCode);
        Assert.HasCount(0, row.ParentActualPurchaseSuppliers);
    }

    /// <summary>父项实际供应商按 SupplierId 聚合，快照 MAX、去重单号和当前有效标记独立于推荐；不增加正剩余筛选的 SQL 边界另由离线测试验证。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ParentActualSuppliers_KeepJsonGroupingAndHistoricalSnapshots(bool deleteSuggestedSupplier)
    {
        using var f = new Fixture();
        var gross = f.Gross(Guid.NewGuid(), "MD", 1); await f.InsertAsync(gross);
        var active = f.Supplier("ACTIVE"); active.IsDeleted = deleteSuggestedSupplier;
        var inactive = f.Supplier("INACTIVE"); inactive.IsActive = false;
        await f.InsertAsync(active); await f.InsertAsync(inactive);
        var parent = f.Net(gross, 1); parent.SupplyTypeSnapshot = "生产"; parent.SuggestedPurchaseSupplierId = active.Uid;
        await f.InsertAsync(parent);
        var child = f.Net(gross, 2); child.RootNetDemandId = parent.Uid; child.ParentNetDemandId = parent.Uid;
        child.DeliveryTargetNetDemandId = parent.Uid; child.DeliveryMode = DemandDeliveryModeEnum.ParentExecutionSupplier;
        await f.InsertAsync(child);
        var a1 = f.Head(active, "A1"); a1.SupplierCode = "A"; a1.SupplierName = "Alpha";
        var a2 = f.Head(active, "A2"); a2.SupplierCode = "B"; a2.SupplierName = "Beta";
        var b1 = f.Head(inactive, "B1"); b1.SupplierCode = "C"; b1.SupplierName = null;
        foreach (var head in new[] { a1, a2, b1 })
        {
            await f.InsertAsync(head); var line = f.Line(head, 1); await f.InsertAsync(line);
            await f.InsertAsync(f.Link(parent, head, line, 5m, 2m));
            if (head == a1) await f.InsertAsync(f.Link(parent, head, line, 1m, 0m));
        }
        var ignored = f.Head(active, "IGNORED"); ignored.LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Voided;
        await f.InsertAsync(ignored); var ignoredLine = f.Line(ignored, 1); await f.InsertAsync(ignoredLine);
        await f.InsertAsync(f.Link(parent, ignored, ignoredLine, 1m, 0m));
        var row = (await f.Repository.GetPurchaseCandidatesAsync(f.Query(gross))).Single();
        Assert.AreEqual(active.Uid, row.SuggestedDeliverySupplierId);
        Assert.HasCount(2, row.ParentActualPurchaseSuppliers);
        Assert.AreEqual(active.Uid, row.ParentActualPurchaseSuppliers[0].SupplierId, "外层按 MAX 编号再身份排序。");
        var a = row.ParentActualPurchaseSuppliers.Single(s => s.SupplierId == active.Uid);
        Assert.AreEqual("B", a.SupplierCode); Assert.AreEqual("Beta", a.SupplierName); Assert.AreEqual(!deleteSuggestedSupplier, a.IsActive);
        CollectionAssert.AreEquivalent(new[] { a1.OrderNo, a2.OrderNo }, a.PurchaseOrderNos.Split(','));
        var b = row.ParentActualPurchaseSuppliers.Single(s => s.SupplierId == inactive.Uid);
        Assert.IsFalse(b.IsActive); Assert.IsNull(b.SupplierName); Assert.AreEqual(b1.OrderNo, b.PurchaseOrderNos);
        Assert.AreEqual(0m, row.AssociatedPurchaseQty, "父项关联不计入子件已采购量。");
    }

    /// <summary>采购来源也保留类型＋Uid，全部模式不分页；零量/非采购/历史物料和历史来源排除，失配源证据不新增拦截。</summary>
    [TestMethod]
    public async Task PurchaseGroupsAndRows_KeepWholeSourceAndEligibility()
    {
        using var f = new Fixture();
        var source = Guid.NewGuid();
        var gross = f.Gross(source, "MD", 1); await f.InsertAsync(gross);
        var ids = new List<Guid>();
        for (var i = 0; i < 205; i++) { var net = f.Net(gross, i + 1); await f.InsertAsync(net); ids.Add(net.Uid); }
        var collision = f.Gross(source, "SO", 1); collision.SourceVersion = null; await f.InsertAsync(collision);
        var other = f.Net(collision, 1); await f.InsertAsync(other);
        var zero = f.Net(gross, 206); zero.NetQty = 0; await f.InsertAsync(zero);
        var manufacture = f.Net(gross, 207); manufacture.SupplyTypeSnapshot = "生产"; await f.InsertAsync(manufacture);
        var history = f.Net(gross, 208); history.LifecycleStatus = NetDemandLifecycleStatusEnum.RolledBack; await f.InsertAsync(history);
        var deleted = f.Net(gross, 209); deleted.IsDeleted = true; await f.InsertAsync(deleted);
        var closed = f.Gross(Guid.NewGuid(), "MD", 1); closed.LifecycleStatus = GrossDemandLifecycleStatusEnum.Closed;
        await f.InsertAsync(closed); await f.InsertAsync(f.Net(closed, 1));
        var rows = await f.Repository.GetPurchaseCandidatesAsync(f.Query(gross));
        Assert.HasCount(205, rows); CollectionAssert.AreEquivalent(ids, rows.Select(r => r.Uid).ToList());
        Assert.AreEqual(other.Uid, (await f.Repository.GetPurchaseCandidatesAsync(f.Query(collision))).Single().Uid);
        var groups = (await f.Repository.GetPurchaseCandidateSourceGroupsAsync()).Where(g => g.SourceUid == source).ToList();
        Assert.HasCount(2, groups); Assert.AreEqual(205, groups.Single(g => g.SourceType == "MD").CandidateCount);
        var all = await f.Repository.GetPurchaseCandidatesAsync(new());
        Assert.AreEqual(206, all.Count(r => ids.Contains(r.Uid) || r.Uid == other.Uid));
        Assert.IsFalse(all.Any(r => r.GrossDemandId == closed.Uid));
    }

    /// <summary>每例在固定故障测试库开启单事务；随机夹具只在该事务内可见，Dispose 的 finally 保证回滚与会话释放。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly string Prefix = "__M14_" + Guid.NewGuid().ToString("N") + "_";
        public readonly DbSession Session;
        public readonly RepositoryBaseContext Context;
        public readonly PostgreSqlMaterialDemandSourceRepository Repository;
        public Fixture()
        {
            Session = new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
            try
            {
                Session.Transaction = Session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
                var user = new Identity();
                Context = new(Session, NullLoggerFactory.Instance, new AuditStampService(user), user);
                Repository = new(Context);
            }
            catch { Session.Dispose(); throw; }
        }

        /// <summary>仅插入本例新实体；使用现有 PG 元数据、参数和审计，不创建结构或更新既有业务行。</summary>
        public async Task InsertAsync<T>(T entity) where T : BaseEntity
        {
            Context.AuditStamp.StampForInsert(entity);
            var metadata = EntityMetadataCache.GetMetadata(typeof(T));
            var table = $"{PostgreSqlNames.Quote(metadata.SchemaName)}.{PostgreSqlNames.Quote(metadata.TableName)}";
            var columns = string.Join(", ", metadata.Columns.Select(c => PostgreSqlNames.Quote(c.ColumnName)));
            var parameters = string.Join(", ", metadata.Columns.Select(c => "@" + c.ParameterName));
            Assert.AreEqual(1, await Session.Connection.ExecuteAsync(new CommandDefinition(
                $"INSERT INTO {table} ({columns}) VALUES ({parameters});", PostgreSqlParameters.Entity(entity, metadata), Session.Transaction)));
        }

        /// <summary>生成本例独立来源；故意不补指纹或客户，确保候选不新增工作台资格。</summary>
        public GrossDemand Gross(Guid source, string type, int line) => new()
        {
            Uid = Guid.NewGuid(), SourceType = type, SourceUid = source, SourceNo = Prefix + "SOURCE",
            SourceLineId = Guid.NewGuid(), SourceLineNumber = line, SkuSnapshot = Prefix + "G" + line, DemandQty = 10m, IsNonStandard = true
        };

        /// <summary>生成可采购物料；根身份自身、单位未知和来源版本未知均按原候选事实保留。</summary>
        public NetDemand Net(GrossDemand gross, int line)
        {
            var id = Guid.NewGuid();
            return new() { Uid = id, GrossDemandId = gross.Uid, RootNetDemandId = id, DecompositionRevision = 1,
                LineNumber = line, SupplyTypeSnapshot = SupplyTypeEnums.采购, SkuSnapshot = Prefix + "N" + line, GrossQty = 10m, NetQty = 10m };
        }

        /// <summary>随机独立供应商编码和必要建档字段，不引用既有主数据。</summary>
        public Supplier Supplier(string suffix) => new() { Uid = Guid.NewGuid(), SupplierCode = Prefix + suffix, SupplierName = suffix, DocRelateNo = Prefix + suffix };

        /// <summary>生成随机采购单号和请求身份，订单供应商字段为历史快照。</summary>
        public PurchaseOrderHead Head(Supplier supplier, string suffix) => new()
        {
            Uid = Guid.NewGuid(), OrderNo = Prefix + suffix, SupplierId = supplier.Uid, SupplierCode = supplier.SupplierCode,
            SupplierName = supplier.SupplierName, ExecutionRequestId = Guid.NewGuid(), OrderDate = new DateTime(2026, 10, 5)
        };

        /// <summary>生成有效目标行；不以单位或数量资格过滤候选。</summary>
        public PurchaseOrderLine Line(PurchaseOrderHead head, int number) => new() { Uid = Guid.NewGuid(), PurchaseOrderId = head.Uid, OrderNo = head.OrderNo, LineNumber = number, Sku = Prefix + "LINE", Quantity = 20m };

        /// <summary>生成符合现有目标约束的独立 PO 贡献身份，不为比较探针写入非法类型。</summary>
        public DemandExecutionLink Link(NetDemand net, PurchaseOrderHead head, PurchaseOrderLine line, decimal allocated, decimal released) => new()
        {
            Uid = Guid.NewGuid(), NetDemandId = net.Uid, ExecutionRequestId = Guid.NewGuid(), CommandItemId = Guid.NewGuid(),
            TargetType = "PO", TargetHeadId = head.Uid, TargetLineId = line.Uid, TargetNo = head.OrderNo,
            AllocatedQty = allocated, ReleasedQty = released
        };

        /// <summary>指定类型与 Uid，验证仓储整体来源返回。</summary>
        public PurchaseDraftCandidateQuery Query(GrossDemand gross) => new() { SourceType = gross.SourceType, SourceUid = gross.SourceUid };

        /// <summary>成功和失败均回滚唯一夹具事务；回滚异常也必须释放事务及连接，不提交、不 DELETE。</summary>
        public void Dispose()
        {
            try
            {
                if (Session.Transaction is { } transaction)
                {
                    try { transaction.Rollback(); }
                    finally { transaction.Dispose(); Session.Transaction = null; }
                }
            }
            finally { Session.Dispose(); }
        }
    }

    /// <summary>独立测试审计身份，不冒用真实登录账号。</summary>
    private sealed class Identity : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "物料来源迁移回滚夹具";
        public bool IsAuthenticated => false;
    }
}
