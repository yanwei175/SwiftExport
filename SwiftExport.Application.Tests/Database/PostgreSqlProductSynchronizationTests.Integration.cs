using Dapper;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Products.Catalog;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>产品资料同步实库回归；固定故障保留库、随机夹具全程回滚，不提交或改动旧记录。</summary>
[TestClass]
public sealed class PostgreSqlProductSynchronizationIntegrationTests
{
    /// <summary>两种选集入口的五组字段边界，包含停用、排除软删和失效父；重复同步不增加版本或审计。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductSynchronizationIntegration")]
    [DataRow(false, 1)]
    [DataRow(false, 2)]
    [DataRow(false, 4)]
    [DataRow(false, 8)]
    [DataRow(false, 15)]
    [DataRow(true, 1)]
    [DataRow(true, 2)]
    [DataRow(true, 4)]
    [DataRow(true, 8)]
    [DataRow(true, 15)]
    public Task SelectedSynchronization_OnlyChangesRequestedGroupsAndRows(bool pull, int flags) => InRollback(async (context, repository) =>
    {
        var fields = (ProductMasterSyncFields)flags;
        var master = await AddMaster(context);
        var active = await AddProduct(context, master.Uid);
        var inactive = await AddProduct(context, master.Uid, active: false);
        var deleted = await AddProduct(context, master.Uid, deleted: true);
        var other = await AddProduct(context, (await AddMaster(context)).Uid);
        var unlinked = await AddProduct(context, null);
        var missing = await AddProduct(context, Guid.NewGuid());
        var deletedMaster = await AddMaster(context);
        var hidden = await AddProduct(context, deletedMaster.Uid);
        Assert.IsTrue((await new PostgreSqlDapperRepository<ProductMaster>(context).DeleteAsync(deletedMaster.Uid, 1)).Success);
        var sibling = await AddProduct(context, master.Uid);
        var equal = await AddProduct(context, master.Uid);
        CopySelectedValues(equal, master);
        Assert.IsTrue((await new PostgreSqlDapperRepository<Product>(context).UpdateAsync(equal)).Success);
        var untouched = new[] { deleted, other, unlinked, missing, hidden, equal }.Concat(pull ? new[] { sibling } : []).ToArray();
        var before = new Dictionary<Guid, string>();
        foreach (var item in untouched) before[item.Uid] = await Snapshot(context, item.Uid);
        var ids = pull ? new[] { active.Uid, inactive.Uid, deleted.Uid, unlinked.Uid, missing.Uid, hidden.Uid, equal.Uid }
            : new[] { master.Uid, deletedMaster.Uid };
        var expectedCount = pull ? 2 : 3;
        Assert.AreEqual(expectedCount, pull ? await repository.PullSelectedFieldsFromMastersAsync(ids, fields)
            : await repository.SyncSelectedFieldsFromMastersAsync(ids, fields));
        foreach (var original in new[] { active, inactive }.Concat(pull ? [] : new[] { sibling }))
        {
            var saved = (await new PostgreSqlDapperRepository<Product>(context).GetByIdAsync(original.Uid))!;
            Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Descriptions) ? master.DefaultDescriptionEn : original.DescriptionEn, saved.DescriptionEn);
            Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Descriptions) ? master.DefaultDescriptionZh : original.DescriptionZh, saved.DescriptionZh);
            Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Features) ? master.DefaultFeaturesEn : original.FeaturesEn, saved.FeaturesEn);
            Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Features) ? master.DefaultFeaturesZh : original.FeaturesZh, saved.FeaturesZh);
            Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Supplier) ? master.DefaultSupplierId : original.DefaultSupplierId, saved.DefaultSupplierId);
            Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.WeightVolume) ? master.DefaultGrossWeight : original.UnitWeight, saved.UnitWeight);
            Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.WeightVolume) ? master.DefaultCbm : original.UnitCBM, saved.UnitCBM);
            Assert.AreEqual(2, saved.Version);
            Assert.AreEqual(original.MasterSku, saved.MasterSku);
            AssertUnselectedProductIdentity(original, saved);
        }
        foreach (var item in untouched) Assert.AreEqual(before[item.Uid], await Snapshot(context, item.Uid));
        var changed = await Snapshot(context, active.Uid);
        Assert.AreEqual(0, pull ? await repository.PullSelectedFieldsFromMastersAsync(ids, fields)
            : await repository.SyncSelectedFieldsFromMastersAsync(ids, fields));
        Assert.AreEqual(changed, await Snapshot(context, active.Uid), "无实际变化时审计和 Version 也不能改变。");
    });

    /// <summary>源值同步始终更新型号编码，勾选描述时同时同步配置；不要求源父行有效，空 Guid 供应商清为 NULL。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductSynchronizationIntegration")]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public Task SourceSynchronization_PreservesSourceAndOptionBoundaries(bool descriptions, bool supplier) => InRollback(async (context, repository) =>
    {
        var source = await AddMaster(context);
        var active = await AddProduct(context, source.Uid);
        var inactive = await AddProduct(context, source.Uid, active: false);
        var deleted = await AddProduct(context, source.Uid, deleted: true);
        var other = await AddProduct(context, (await AddMaster(context)).Uid);
        var beforeDeleted = await Snapshot(context, deleted.Uid);
        var beforeOther = await Snapshot(context, other.Uid);
        Assert.IsTrue((await new PostgreSqlDapperRepository<ProductMaster>(context).DeleteAsync(source.Uid, 1)).Success);
        source.MasterSku = " " + source.MasterSku.ToLowerInvariant() + " ";
        source.DefaultDescriptionEn = null;
        source.DefaultDescriptionZh = "";
        source.DefaultFeaturesEn = "配置尾空格 ";
        source.DefaultFeaturesZh = null;
        source.DefaultSupplierId = Guid.Empty;
        Assert.AreEqual(2, await repository.SyncFromProductMasterAsync(source, descriptions, supplier));
        foreach (var original in new[] { active, inactive })
        {
            var saved = (await new PostgreSqlDapperRepository<Product>(context).GetByIdAsync(original.Uid))!;
            Assert.AreEqual(source.MasterSku, saved.MasterSku);
            Assert.AreEqual(descriptions ? null : original.DescriptionEn, saved.DescriptionEn);
            Assert.AreEqual(descriptions ? "" : original.DescriptionZh, saved.DescriptionZh);
            Assert.AreEqual(descriptions ? source.DefaultFeaturesEn : original.FeaturesEn, saved.FeaturesEn);
            Assert.AreEqual(descriptions ? null : original.FeaturesZh, saved.FeaturesZh);
            Assert.AreEqual(supplier ? null : original.DefaultSupplierId, saved.DefaultSupplierId);
            Assert.AreEqual(original.UnitWeight, saved.UnitWeight);
            Assert.AreEqual(original.UnitCBM, saved.UnitCBM);
            Assert.AreEqual(2, saved.Version);
            AssertUnselectedProductIdentity(original, saved);
        }
        Assert.AreEqual(beforeDeleted, await Snapshot(context, deleted.Uid));
        Assert.AreEqual(beforeOther, await Snapshot(context, other.Uid));
        var after = await Snapshot(context, active.Uid);
        Assert.AreEqual(0, await repository.SyncFromProductMasterAsync(source, descriptions, supplier));
        Assert.AreEqual(after, await Snapshot(context, active.Uid));
    });

    /// <summary>C 比较识别仅大小写、尾空格和 U200B 差异；再同步精确一致文本时不更新。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductSynchronizationIntegration")]
    [DataRow("Name", "name")]
    [DataRow("Name ", "Name")]
    [DataRow("Name\u200B", "Name")]
    public Task ExactTextDifference_ChangesVersionOnce(string stored, string target) => InRollback(async (context, repository) =>
    {
        var master = await AddMaster(context);
        var item = await AddProduct(context, master.Uid);
        master.DefaultDescriptionEn = target;
        master.DefaultDescriptionZh = item.DescriptionZh;
        master.DefaultFeaturesEn = item.FeaturesEn;
        master.DefaultFeaturesZh = item.FeaturesZh;
        master.MasterSku = item.MasterSku!;
        await context.Session.Connection.ExecuteAsync("UPDATE public.product SET description_en=@Stored WHERE uid=@Uid",
            new { Stored = stored, item.Uid }, context.Session.Transaction);
        Assert.AreEqual(1, await repository.SyncFromProductMasterAsync(master, true, false));
        Assert.AreEqual(2, (await new PostgreSqlDapperRepository<Product>(context).GetByIdAsync(item.Uid))!.Version);
        Assert.AreEqual(0, await repository.SyncFromProductMasterAsync(master, true, false));
    });

    /// <summary>数据库来源选组支持 NULL 清除和空 Guid→NULL，不将空字符串也转 NULL，装率不用于重量体积换算。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductSynchronizationIntegration")]
    public Task SelectedNullsAndEmptySupplier_ClearExactly() => InRollback(async (context, repository) =>
    {
        var master = await AddMaster(context);
        var item = await AddProduct(context, master.Uid);
        await context.Session.Connection.ExecuteAsync("""
            UPDATE public.product_master SET default_description_en=NULL,default_description_zh='',
                default_features_en=NULL,default_features_zh='',default_supplier_id=@Empty,
                default_gross_weight=NULL,default_cbm=NULL,default_pcs_per_package=8 WHERE uid=@Uid
            """, new { Empty = Guid.Empty, master.Uid }, context.Session.Transaction);
        Assert.AreEqual(1, await repository.SyncSelectedFieldsFromMastersAsync([master.Uid], ProductMasterSyncFields.All));
        var saved = (await new PostgreSqlDapperRepository<Product>(context).GetByIdAsync(item.Uid))!;
        Assert.IsNull(saved.DescriptionEn);
        Assert.AreEqual("", saved.DescriptionZh);
        Assert.IsNull(saved.FeaturesEn);
        Assert.AreEqual("", saved.FeaturesZh);
        Assert.IsNull(saved.DefaultSupplierId);
        Assert.IsNull(saved.UnitWeight);
        Assert.IsNull(saved.UnitCBM);
        Assert.AreEqual(item.MasterSku, saved.MasterSku);
        Assert.AreEqual(0, await repository.SyncSelectedFieldsFromMastersAsync([master.Uid], ProductMasterSyncFields.All));
    });

    /// <summary>供应商专用设置保留同值更新、空 Guid 原值和版本拒绝；实库仍在回滚事务内，不以自动提交测试无事务入口。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductSynchronizationIntegration")]
    public Task SetDefaultSupplier_PreservesSameValueEmptyGuidAndConcurrency() => InRollback(async (context, repository) =>
    {
        var item = await AddProduct(context, (await AddMaster(context)).Uid);
        Assert.AreEqual(2, (await repository.SetDefaultSupplierAsync(item.Uid, 1, item.DefaultSupplierId!.Value)).NewVersion);
        var supplier = Guid.NewGuid();
        Assert.AreEqual(3, (await repository.SetDefaultSupplierAsync(item.Uid, 2, supplier)).NewVersion);
        Assert.AreEqual(supplier, (await new PostgreSqlDapperRepository<Product>(context).GetByIdAsync(item.Uid))!.DefaultSupplierId);
        Assert.AreEqual(4, (await repository.SetDefaultSupplierAsync(item.Uid, 3, supplier)).NewVersion);
        Assert.AreEqual(5, (await repository.SetDefaultSupplierAsync(item.Uid, 4, Guid.Empty)).NewVersion);
        var saved = (await new PostgreSqlDapperRepository<Product>(context).GetByIdAsync(item.Uid))!;
        Assert.AreEqual(Guid.Empty, saved.DefaultSupplierId);
        var before = await Snapshot(context, item.Uid);
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.SetDefaultSupplierAsync(item.Uid, 2, Guid.NewGuid())).ErrorCode);
        Assert.AreEqual(before, await Snapshot(context, item.Uid));
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.SetDefaultSupplierAsync(Guid.NewGuid(), 1, Guid.Empty)).ErrorCode);
        var deleted = await AddProduct(context, null, deleted: true);
        var beforeDeleted = await Snapshot(context, deleted.Uid);
        Assert.IsFalse((await repository.SetDefaultSupplierAsync(deleted.Uid, 1, Guid.Empty)).Success);
        Assert.AreEqual(beforeDeleted, await Snapshot(context, deleted.Uid));
    });

    /// <summary>三个同步入口预取消不修改随机夹具，调用方事务仍可读取；供应商专用入口原契约没有取消参数。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductSynchronizationIntegration")]
    public Task PreCanceledSynchronization_LeavesRowAndCallerTransactionUnchanged() => InRollback(async (context, repository) =>
    {
        var master = await AddMaster(context);
        var item = await AddProduct(context, master.Uid);
        var before = await Snapshot(context, item.Uid);
        var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.SyncFromProductMasterAsync(master, true, true, token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.SyncSelectedFieldsFromMastersAsync([master.Uid], ProductMasterSyncFields.All, token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.PullSelectedFieldsFromMastersAsync([item.Uid], ProductMasterSyncFields.All, token));
        Assert.AreEqual(before, await Snapshot(context, item.Uid));
        Assert.AreEqual(1, await context.Session.Connection.ExecuteScalarAsync<int>("SELECT 1", transaction: context.Session.Transaction));
    });

    /// <summary>只允许随机数据写入本事务，回滚后按随机审计名检查两类夹具零留存。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, PostgreSqlProductRepository, Task> scenario)
    {
        var connectionString = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var session = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        var audit = Prefix();
        var user = new TestUser(audit);
        var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try { await scenario(context, new PostgreSqlProductRepository(context)); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("""
            SELECT (SELECT count(*) FROM public.product WHERE created_by=@Audit)
                 + (SELECT count(*) FROM public.product_master WHERE created_by=@Audit)
            """, new { Audit = audit }));
    }

    /// <summary>随机源包含四组可区分字段，毛重及体积不与装率成一倍关系。</summary>
    private static async Task<ProductMaster> AddMaster(RepositoryBaseContext context)
    {
        var master = new ProductMaster
        {
            MasterSku = Prefix(), DefaultDescriptionEn = "name", DefaultDescriptionZh = "中文",
            DefaultFeaturesEn = null, DefaultFeaturesZh = "", DefaultSupplierId = Guid.NewGuid(),
            DefaultGrossWeight = 12.5m, DefaultCbm = 0.75m, DefaultPcsPerPackage = 4
        };
        Assert.IsTrue((await new PostgreSqlDapperRepository<ProductMaster>(context).AddAsync(master)).Success);
        return master;
    }

    /// <summary>通过基础 CRUD 新增并读回随机物料作为精确比较基准，软删只作用本次夹具；不依赖主仓储其他查询分部。</summary>
    private static async Task<Product> AddProduct(RepositoryBaseContext context, Guid? master, bool active = true, bool deleted = false)
    {
        var item = new Product
        {
            Sku = Prefix(), ProductMasterId = master, MasterSku = "OLD", IsActive = active, SupplyType = "Purchase", Unit = "PCS",
            DescriptionEn = "Name", DescriptionZh = "中文 ", FeaturesEn = "原配置", FeaturesZh = "原中文配置",
            DefaultSupplierId = Guid.Empty, UnitWeight = 1.25m, UnitCBM = 0.15m, USDPrice = 77m, CNYPrice = 88m,
            DefaultWarehouseSiteId = Guid.NewGuid(), ManufacturerId = Guid.NewGuid(), ColorId = Guid.NewGuid(),
            Note = "原备注", MainImage = "原图", LinkSku = "原关联", ManufacturerModel = "原制造商型号"
        };
        var repository = new PostgreSqlDapperRepository<Product>(context);
        Assert.IsTrue((await repository.AddAsync(item)).Success);
        item = (await repository.GetByIdAsync(item.Uid))!;
        Assert.IsNotNull(item);
        if (deleted) Assert.IsTrue((await repository.DeleteAsync(item.Uid, item.Version)).Success);
        return item;
    }

    /// <summary>构造完全一致的资料行，证明所有选组下不发生伪更新。</summary>
    private static void CopySelectedValues(Product item, ProductMaster master)
    {
        item.DescriptionEn = master.DefaultDescriptionEn; item.DescriptionZh = master.DefaultDescriptionZh;
        item.FeaturesEn = master.DefaultFeaturesEn; item.FeaturesZh = master.DefaultFeaturesZh;
        item.DefaultSupplierId = master.DefaultSupplierId; item.UnitWeight = master.DefaultGrossWeight; item.UnitCBM = master.DefaultCbm;
    }

    /// <summary>确认同步不写业务资格、关联身份、价格、库存单位或其他资料字段。</summary>
    private static void AssertUnselectedProductIdentity(Product original, Product saved)
    {
        Assert.AreEqual(original.ProductMasterId, saved.ProductMasterId); Assert.AreEqual(original.Sku, saved.Sku);
        Assert.AreEqual(original.ColorId, saved.ColorId); Assert.AreEqual(original.IsActive, saved.IsActive);
        Assert.AreEqual(original.Unit, saved.Unit); Assert.AreEqual(original.SupplyType, saved.SupplyType);
        Assert.AreEqual(original.USDPrice, saved.USDPrice); Assert.AreEqual(original.CNYPrice, saved.CNYPrice);
        Assert.AreEqual(original.DefaultWarehouseSiteId, saved.DefaultWarehouseSiteId); Assert.AreEqual(original.ManufacturerId, saved.ManufacturerId);
        Assert.AreEqual(original.MainImage, saved.MainImage); Assert.AreEqual(original.Note, saved.Note);
        Assert.AreEqual(original.LinkSku, saved.LinkSku); Assert.AreEqual(original.ManufacturerModel, saved.ManufacturerModel);
        Assert.AreEqual(original.CreatedDate, saved.CreatedDate);
        Assert.AreEqual(original.CreatedBy, saved.CreatedBy);
    }

    /// <summary>整行只读快照确认未命中和无变化行不修改任何字段，包括审计。</summary>
    private static Task<string> Snapshot(RepositoryBaseContext context, Guid uid) => context.Session.Connection.QuerySingleAsync<string>(
        "SELECT to_jsonb(p)::text FROM public.product p WHERE uid=@Uid", new { Uid = uid }, context.Session.Transaction);
    /// <summary>随机短编码避免与既有主数据相交。</summary>
    private static string Prefix() => "PGPS" + Guid.NewGuid().ToString("N")[..12];
    /// <summary>仅提供本例随机审计名。</summary>
    private sealed class TestUser(string audit) : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => audit;
        public bool IsAuthenticated => false;
    }
}
