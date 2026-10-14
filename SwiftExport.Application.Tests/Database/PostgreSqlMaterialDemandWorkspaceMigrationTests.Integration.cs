using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Query;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>主控串行运行的真实 PG 工作台验收；固定故障库门禁和随机夹具，单事务 finally 回滚，不改变公共结构或既有数据。</summary>
[TestClass]
[DoNotParallelize]
[TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlMaterialDemandWorkspaceMigrationIntegrationTests
{
    /// <summary>全部快捷视图先筛选再统计/取页；只有一个匹配时第二页为空但总数仍为1，未知快捷值按All。</summary>
    [TestMethod]
    [DataRow("All", 6)] [DataRow("Unknown", 6)] [DataRow("Unassigned", 3)]
    [DataRow("Partial", 1)] [DataRow("Assigned", 1)] [DataRow("Overdue", 1)]
    [DataRow("DueSoon", 1)] [DataRow("Review", 1)]
    public async Task QuickViews_CountAndPageUseSameFilteredWorkspace(string quick, int expected)
    {
        using var f = new Fixture(); var gross = f.Gross(); await f.InsertAsync(gross);
        var day = Fixture.BusinessDate(); var rows = new List<NetDemand>();
        for (var i = 1; i <= 6; i++)
        {
            var net = f.Net(gross, i); net.RequiredDate = i switch { 1 => day.AddDays(-2), 2 => day.AddDays(7), 3 => day.AddDays(-1), 4 => day.AddDays(-1), 5 => day.AddDays(8), _ => null };
            if (i == 4) net.UnitSnapshot = null;
            await f.InsertAsync(net); rows.Add(net);
            if (i == 2) await f.PurchaseAsync(gross, net, 5m, 2m);
            if (i == 3) await f.PurchaseAsync(gross, net, 12m, 2m);
        }
        var result = await f.Repository.SearchManagerAsync(new() { NetDemandIds = rows.Select(r => r.Uid).ToList(), PageIndex = 2, PageSize = 1, QuickView = quick });
        Assert.AreEqual(expected, result.TotalCount); Assert.AreEqual(expected > 1 ? 1 : 0, result.Items.Count);
        var full = await f.Repository.SearchManagerAsync(new() { SourceUid = gross.SourceUid, QuickView = quick, PageSize = 1 });
        Assert.AreEqual(expected, full.TotalCount); Assert.AreEqual(expected, full.Items.Count);
        Assert.AreEqual(1, full.PageIndex); Assert.AreEqual(expected, full.PageSize);
        if (quick == "Partial") Assert.AreEqual(3m, full.Items.Single().AssociatedPurchaseQty);
        if (quick == "Assigned") { Assert.AreEqual(10m, full.Items.Single().AssociatedPurchaseQty); Assert.AreEqual("", full.Items.Single().DueHint); }
        if (quick == "Review") { Assert.IsNull(full.Items.Single().AssociatedPurchaseQty); Assert.IsNull(full.Items.Single().RemainingQty); Assert.AreEqual("", full.Items.Single().DueHint); }
    }

    /// <summary>整来源返回206行并跨类型，来源导航仅Uid一组；普通超末页仍返回205总数。</summary>
    [TestMethod]
    public async Task WholeSourceAndGroups_KeepNoCapAndUidOnlyGrouping()
    {
        using var f = new Fixture(); var gross = f.Gross(); await f.InsertAsync(gross);
        var ids = new List<Guid>();
        for (var i = 1; i <= 205; i++) { var net = f.Net(gross, i); await f.InsertAsync(net); ids.Add(net.Uid); }
        var other = f.Gross(); other.SourceUid = gross.SourceUid; other.SourceType = "SO"; other.SourceVersion = 3; await f.InsertAsync(other);
        var sibling = f.Net(other, 1); sibling.SourceVersionSnapshot = 3; await f.InsertAsync(sibling);
        var all = await f.Repository.SearchManagerAsync(new() { SourceUid = gross.SourceUid, PageIndex = 99, PageSize = 1 });
        Assert.AreEqual(206, all.TotalCount); Assert.HasCount(206, all.Items); Assert.AreEqual(206, all.PageSize); Assert.AreEqual(1, all.PageIndex);
        var group = (await f.Repository.SearchMaterialSourceGroupsAsync(new())).Single(g => g.SourceUid == gross.SourceUid);
        Assert.AreEqual(206, group.DemandCount); Assert.AreEqual("SO", group.SourceType);
        var page = await f.Repository.SearchManagerAsync(new() { NetDemandIds = ids, PageIndex = 999, PageSize = 2 });
        Assert.AreEqual(205, page.TotalCount); Assert.HasCount(0, page.Items);
        var empty = await f.Repository.SearchManagerAsync(new() { SourceUid = Guid.NewGuid() });
        Assert.AreEqual(0, empty.TotalCount); Assert.HasCount(0, empty.Items); Assert.AreEqual(1, empty.PageSize);
    }

    /// <summary>未知原因按原优先级返回null关联量和空到期提示；不把客户/版本/单位/目标/超量问题统一成0。</summary>
    [TestMethod]
    [DataRow("customer", "来源客户缺失")]
    [DataRow("demand-version", "来源版本或需求指纹需核实")]
    [DataRow("fingerprint", "来源版本或需求指纹需核实")]
    [DataRow("fingerprint-mismatch", "来源版本或需求指纹需核实")]
    [DataRow("so-version", "来源版本或需求指纹需核实")]
    [DataRow("md-version", "来源版本或需求指纹需核实")]
    [DataRow("unit", "物料单位需核实")]
    [DataRow("quantity", "物料需求数量需核实")]
    [DataRow("missing-target", "采购目标、客户或单位需核实")]
    [DataRow("wrong-customer", "采购目标、客户或单位需核实")]
    [DataRow("wrong-unit", "采购目标、客户或单位需核实")]
    [DataRow("history-head", "采购目标、客户或单位需核实")]
    [DataRow("over", "采购承接超量需核实")]
    public async Task ReviewReasons_PreserveNullableQuantities(string branch, string reason)
    {
        using var f = new Fixture(); var gross = f.Gross();
        if (branch == "customer") gross.CustomerId = null;
        if (branch == "fingerprint") gross.DemandBasisFingerprint = null;
        if (branch == "so-version") gross.SourceType = "SO";
        await f.InsertAsync(gross);
        var net = f.Net(gross, 1); net.RequiredDate = Fixture.BusinessDate().AddDays(-1);
        if (branch == "demand-version") net.SourceDemandVersionSnapshot = null;
        if (branch == "fingerprint-mismatch") net.SourceDemandBasisFingerprintSnapshot = new string('B', 64);
        if (branch == "md-version") net.SourceVersionSnapshot = 1;
        if (branch == "unit") net.UnitSnapshot = "   ";
        if (branch == "quantity") net.NetQty = 0;
        await f.InsertAsync(net);
        if (branch == "missing-target")
            await f.InsertAsync(f.Link(net, Guid.NewGuid(), Guid.NewGuid(), 5m, 2m));
        if (branch is "wrong-customer" or "wrong-unit" or "history-head" or "over")
            await f.PurchaseAsync(gross, net, branch == "over" ? 20m : 5m, 2m,
                branch == "wrong-customer", branch == "wrong-unit", branch == "history-head");
        var row = (await f.Repository.SearchManagerAsync(new() { NetDemandIds = [net.Uid] })).Items.Single();
        Assert.AreEqual(reason, row.ReviewReason); Assert.AreEqual("需核实", row.ProcurementStatus);
        Assert.IsNull(row.AssociatedPurchaseQty); Assert.IsNull(row.RemainingQty); Assert.AreEqual("", row.DueHint);
    }

    /// <summary>有效物料默认不读取关闭来源；历史模式读取关闭来源并保留历史采购筛选，删除来源仍排除。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task HistoricalMode_PreservesSourceAndActualPurchaseFilters(bool deletedSource)
    {
        using var f = new Fixture(); var gross = f.Gross(); gross.LifecycleStatus = GrossDemandLifecycleStatusEnum.Closed; gross.IsDeleted = deletedSource; await f.InsertAsync(gross);
        var net = f.Net(gross, 1); net.LifecycleStatus = NetDemandLifecycleStatusEnum.RolledBack; await f.InsertAsync(net);
        var purchase = await f.PurchaseAsync(gross, net, 5m, 5m, historicalHead: true, cancelled: true);
        var active = await f.Repository.SearchManagerAsync(new() { NetDemandIds = [net.Uid] }); Assert.AreEqual(0, active.TotalCount);
        var result = await f.Repository.SearchManagerAsync(new() { NetDemandIds = [net.Uid], LifecycleStatus = NetDemandLifecycleStatusEnum.RolledBack, ActualPurchaseSupplierId = purchase.SupplierId, PurchaseOrderNo = " " + purchase.OrderNo + " " });
        Assert.AreEqual(deletedSource ? 0 : 1, result.TotalCount);
        if (!deletedSource)
        {
            var row = result.Items.Single(); Assert.AreEqual("历史", row.ProcurementStatus); Assert.AreEqual("", row.ReviewReason);
            Assert.IsNull(row.AssociatedPurchaseQty); Assert.AreEqual("", row.DueHint);
        }
        var groups = await f.Repository.SearchMaterialSourceGroupsAsync(new() { LifecycleStatus = NetDemandLifecycleStatusEnum.RolledBack });
        Assert.AreEqual(!deletedSource, groups.Any(g => g.SourceUid == gross.SourceUid));
    }

    /// <summary>字面模式保留特殊字符、空格通配、半角问号及大小写；方括号不恢复原始字符组语义。</summary>
    [TestMethod]
    [DataRow("X[ab]Y", "[ab]", 1)] [DataRow("X[ab]Y", "[cd]", 0)]
    [DataRow("X~[Y", "~[", 1)] [DataRow("X%Y", "%", 1)]
    [DataRow("X_Y", "_", 1)] [DataRow("Xfoo--barY", "foo bar", 1)]
    [DataRow("XabcY", "a?c", 1)] [DataRow("X]Y", "]", 1)]
    [DataRow("X\\Y", "\\", 1)] [DataRow("XCaseY", "case", 1)]
    public async Task LiteralKeyword_UsesOriginalPatternRules(string value, string keyword, int count)
    {
        using var f = new Fixture(); var gross = f.Gross(); gross.SourceNo = "SOURCE"; await f.InsertAsync(gross);
        var net = f.Net(gross, 1); net.SkuSnapshot = value; await f.InsertAsync(net);
        var result = await f.Repository.SearchManagerAsync(new() { NetDemandIds = [net.Uid], Keyword = keyword });
        Assert.AreEqual(count, result.TotalCount); Assert.AreEqual(count, result.Items.Count);
    }

    /// <summary>图片区分客供/普通物料，LinkSku可以来源回退；停用主数据仍展示，未知单位不回退，三个未读取商品建议字段保持null。</summary>
    [TestMethod]
    [DataRow(false, null)] [DataRow(false, " KG ")] [DataRow(true, "\t")]
    public async Task ImageUnitAndSuggestions_KeepManagerSpecificProjection(bool supplied, string? unit)
    {
        using var f = new Fixture(); var gross = f.Gross(); gross.ImagePathSnapshot = " 父来源图 "; gross.LinkSkuSnapshot = "来源型号"; gross.UnitSnapshot = "SET"; await f.InsertAsync(gross);
        var supplier = f.Supplier(); supplier.IsActive = false; await f.InsertAsync(supplier);
        var product = new Product { Uid = Guid.NewGuid(), Sku = f.Prefix + "P", SupplyType = SupplyTypeEnums.采购, Unit = "CURRENT", MainImage = "当前物料图", ProductNeutralId = null, IsActive = false }; await f.InsertAsync(product);
        var parent = f.Net(gross, 1); parent.SuggestedPurchaseSupplierId = supplier.Uid; await f.InsertAsync(parent);
        var net = f.Net(gross, 2); net.ProductId = product.Uid; net.UnitSnapshot = unit; net.SuggestedPurchaseSupplierId = supplier.Uid;
        if (supplied) { net.RootNetDemandId = parent.Uid; net.ParentNetDemandId = parent.Uid; net.DeliveryTargetNetDemandId = parent.Uid; net.DeliveryMode = DemandDeliveryModeEnum.ParentExecutionSupplier; }
        await f.InsertAsync(net);
        var row = (await f.Repository.SearchManagerAsync(new() { NetDemandIds = [net.Uid] })).Items.Single();
        Assert.AreEqual(supplied ? "当前物料图" : "父来源图", row.ImagePath); Assert.AreEqual("来源型号", row.LinkSku);
        Assert.AreEqual(unit is null ? null : unit.Trim(' '), row.Unit); Assert.AreEqual(supplier.SupplierCode, row.SuggestedPurchaseSupplierCode);
        Assert.IsNull(row.SuggestedSupplierSku); Assert.IsNull(row.SuggestedUnitPrice); Assert.IsNull(row.SuggestedTaxPrice);
        Assert.AreEqual(supplied ? supplier.Uid : (Guid?)null, row.SuggestedDeliverySupplierId);
        Assert.IsNull(row.SuggestedDeliverySupplierHint); Assert.AreEqual(supplied, row.IsCustomerSupplied);
    }

    /// <summary>父目标必须同父、来源、根、修订且有效；缺建议/删除供应商使用各自提示，提示不替代实际采购事实。</summary>
    [TestMethod]
    [DataRow("parent", "送达目标关系无效")] [DataRow("gross", "送达目标关系无效")]
    [DataRow("root", "送达目标关系无效")] [DataRow("revision", "送达目标关系无效")]
    [DataRow("history", "送达目标关系无效")] [DataRow("deleted", "送达目标关系无效")]
    [DataRow("no-suggestion", "父件尚未设置推荐采购供应商")]
    [DataRow("deleted-supplier", "父件推荐采购供应商已失效")]
    public async Task DeliveryTargetHints_PreserveStableTreeBoundary(string branch, string hint)
    {
        using var f = new Fixture(); var gross = f.Gross(); await f.InsertAsync(gross);
        var supplier = f.Supplier(); if (branch == "deleted-supplier") supplier.IsDeleted = true; await f.InsertAsync(supplier);
        var parent = f.Net(gross, 1); parent.SuggestedPurchaseSupplierId = supplier.Uid;
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
            case "no-suggestion": parent.SuggestedPurchaseSupplierId = null; break;
        }
        await f.InsertAsync(parent); await f.InsertAsync(child);
        var row = (await f.Repository.SearchManagerAsync(new() { NetDemandIds = [child.Uid] })).Items.Single();
        Assert.AreEqual(hint, row.SuggestedDeliverySupplierHint); Assert.IsNull(row.SuggestedDeliverySupplierCode);
        Assert.AreEqual(0m, row.AssociatedPurchaseQty, "推荐提示不等于采购量未知或已经采购。");
    }

    /// <summary>组合数组、日期含时间/次日排他、非标/客供、建议/实际供应商和单号条件共同生效，不增目标行过滤。</summary>
    [TestMethod]
    public async Task CombinedFilters_KeepSupplierAndDateSemantics()
    {
        using var f = new Fixture(); var gross = f.Gross(); await f.InsertAsync(gross);
        var supplier = f.Supplier(); supplier.SupplierName = "Supplier[ab]"; supplier.IsActive = false; await f.InsertAsync(supplier);
        var first = f.Net(gross, 1); first.SkuSnapshot = "SKU"; first.CustomerSkuSnapshot = "CUSTOMER"; first.SuggestedPurchaseSupplierId = supplier.Uid; first.RequiredDate = new DateTime(2026, 10, 5, 12, 0, 0); await f.InsertAsync(first);
        var head = await f.PurchaseAsync(gross, first, 5m, 2m, supplier: supplier);
        var second = f.Net(gross, 2); second.RequiredDate = new DateTime(2026, 10, 6); await f.InsertAsync(second);
        var request = new MaterialDemandManagerSearchRequest { NetDemandIds = [first.Uid, first.Uid, second.Uid], GrossDemandIds = [gross.Uid], SourceUids = [gross.SourceUid], SourceTypes = ["md "], SourceNos = [gross.SourceNo], Skus = ["sku"], CustomerSkus = ["customer"], SuggestedPurchaseSupplierId = supplier.Uid, SuggestedPurchaseSupplierKeyword = "[ab]", ActualPurchaseSupplierId = supplier.Uid, ActualPurchaseSupplierKeyword = "[ab]", PurchaseOrderNo = " " + head.OrderNo + " ", DemandRequiredDateFrom = new DateTime(2026, 10, 5, 11, 0, 0), DemandRequiredDateTo = new DateTime(2026, 10, 5, 23, 0, 0), IsNonStandard = true, IsCustomerSupplied = false };
        var response = await f.Repository.SearchManagerAsync(request); Assert.AreEqual(1, response.TotalCount); Assert.AreEqual(first.Uid, response.Items.Single().Uid);
        request.DemandRequiredDateFrom = new DateTime(2026, 10, 5, 13, 0, 0);
        Assert.AreEqual(0, (await f.Repository.SearchManagerAsync(request)).TotalCount);
        request.DemandRequiredDateFrom = null; request.PurchaseOrderNo = "missing";
        Assert.AreEqual(0, (await f.Repository.SearchManagerAsync(request)).TotalCount);
    }

    /// <summary>每例独立故障库事务，查询借用同一已写入夹具的事务；Dispose最终回滚，不提交/清库。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly string Prefix = "__M14W_" + Guid.NewGuid().ToString("N") + "_";
        public readonly Guid CustomerId = Guid.NewGuid();
        public readonly DbSession Session;
        public readonly RepositoryBaseContext Context;
        public readonly PostgreSqlMaterialDemandQueryRepository Repository;
        public Fixture()
        {
            Session = new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
            try
            {
                Session.Transaction = Session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
                var user = new Identity(); Context = new(Session, NullLoggerFactory.Instance, new AuditStampService(user), user); Repository = new(Context);
            }
            catch { Session.Dispose(); throw; }
        }

        /// <summary>按原墙上时间规则获得一次业务日；场景用同一日创建到期边界。</summary>
        public static DateTime BusinessDate() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("China Standard Time")).Date;

        /// <summary>只插入随机夹具实体，复用既有PG元数据与参数，不修改结构或历史行。</summary>
        public async Task InsertAsync<T>(T entity) where T : BaseEntity
        {
            Context.AuditStamp.StampForInsert(entity); var map = EntityMetadataCache.GetMetadata(typeof(T));
            var columns = string.Join(", ", map.Columns.Select(c => PostgreSqlNames.Quote(c.ColumnName)));
            var parameters = string.Join(", ", map.Columns.Select(c => "@" + c.ParameterName));
            Assert.AreEqual(1, await Session.Connection.ExecuteAsync(new CommandDefinition($"INSERT INTO {PostgreSqlNames.Quote(map.SchemaName)}.{PostgreSqlNames.Quote(map.TableName)} ({columns}) VALUES ({parameters});", PostgreSqlParameters.Entity(entity, map), Session.Transaction)));
        }

        /// <summary>生成可证明的手工来源；客户身份不依赖当前主数据存在，保持源码只检查权威CustomerId的边界。</summary>
        public GrossDemand Gross() => new() { Uid = Guid.NewGuid(), SourceType = "MD", SourceUid = Guid.NewGuid(), SourceLineId = Guid.NewGuid(), SourceLineNumber = 1, SourceNo = Prefix + "SOURCE", SkuSnapshot = "GROSS", DemandQty = 10m, CustomerId = CustomerId, IsNonStandard = true, DemandBasisFingerprint = new string('A', 64) };

        /// <summary>生成单位和来源快照可信的工作台行，根指向自身，所有日期默认空。</summary>
        public NetDemand Net(GrossDemand gross, int line)
        {
            var id = Guid.NewGuid(); return new() { Uid = id, GrossDemandId = gross.Uid, RootNetDemandId = id, LineNumber = line, DecompositionRevision = 1, SourceDemandVersionSnapshot = gross.Version, SourceDemandBasisFingerprintSnapshot = gross.DemandBasisFingerprint, SkuSnapshot = "NET", SupplyTypeSnapshot = SupplyTypeEnums.采购, UnitSnapshot = "KG", GrossQty = 10m, NetQty = 10m };
        }

        /// <summary>创建独立供应商档案，未删状态不代表新业务资格。</summary>
        public Supplier Supplier() => new() { Uid = Guid.NewGuid(), SupplierCode = Prefix + Guid.NewGuid().ToString("N"), SupplierName = "供应商", DocRelateNo = Prefix + "DOC" };

        /// <summary>按现有CHECK创建合法Link；历史退出使用取消且全释放，不构造非法软删或负贡献。</summary>
        public DemandExecutionLink Link(NetDemand net, Guid head, Guid line, decimal allocated, decimal released) => new() { Uid = Guid.NewGuid(), NetDemandId = net.Uid, ExecutionRequestId = Guid.NewGuid(), CommandItemId = Guid.NewGuid(), TargetType = "PO", TargetHeadId = head, TargetLineId = line, TargetNo = Prefix + "PO", AllocatedQty = allocated, ReleasedQty = released };

        /// <summary>创建合法采购头行和贡献；仅由场景参数改变可核验客户、单位或历史头，不绕过约束。</summary>
        public async Task<PurchaseOrderHead> PurchaseAsync(GrossDemand gross, NetDemand net, decimal allocated, decimal released,
            bool wrongCustomer = false, bool wrongUnit = false, bool historicalHead = false, bool cancelled = false, Supplier? supplier = null)
        {
            if (supplier is null) { supplier = Supplier(); await InsertAsync(supplier); }
            var head = new PurchaseOrderHead { Uid = Guid.NewGuid(), OrderNo = Prefix + Guid.NewGuid().ToString("N"), SupplierId = supplier.Uid, SupplierCode = supplier.SupplierCode, SupplierName = supplier.SupplierName, CustomerId = wrongCustomer ? Guid.NewGuid() : gross.CustomerId, ExecutionRequestId = Guid.NewGuid(), OrderDate = new DateTime(2026, 10, 5), LifecycleStatus = historicalHead ? PurchaseOrderLifecycleStatusEnum.Voided : PurchaseOrderLifecycleStatusEnum.Active };
            await InsertAsync(head); var line = new PurchaseOrderLine { Uid = Guid.NewGuid(), PurchaseOrderId = head.Uid, OrderNo = head.OrderNo, LineNumber = 1, Sku = "LINE", Quantity = 20m, UnitSnapshot = wrongUnit ? "BOX" : net.UnitSnapshot }; await InsertAsync(line);
            var link = Link(net, head.Uid, line.Uid, allocated, released); if (cancelled) link.LinkStatus = DemandExecutionLinkStatusEnum.Cancelled;
            await InsertAsync(link); return head;
        }

        /// <summary>成功或异常均回滚唯一夹具事务；回滚失败也释放连接，不执行提交、删除或结构清理。</summary>
        public void Dispose()
        {
            try { if (Session.Transaction is { } tx) { try { tx.Rollback(); } finally { tx.Dispose(); Session.Transaction = null; } } }
            finally { Session.Dispose(); }
        }
    }

    /// <summary>夹具审计身份，不冒用真实用户或记录连接秘密。</summary>
    private sealed class Identity : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "物料工作台回滚夹具";
        public bool IsAuthenticated => false;
    }
}
