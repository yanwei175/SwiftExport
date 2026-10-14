using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Suppliers.SupplierProducts;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>供应商产品真实 PostgreSQL 读取验收；固定专用库、随机事务夹具全回滚，不提交、改旧数据或 DDL。</summary>
[TestClass]
[TestCategory("PostgreSqlSupplierProductIntegration")]
public sealed class PostgreSqlSupplierProductMigrationIntegrationTests
{
    /// <summary>DTO 使用当前联表值，采购实体使用关系快照；未关联、缺失及软删 Product 不隐藏关系，供应商软删才隐藏 DTO。</summary>
    [TestMethod]
    public Task SupplierProduct_DisplayAndSnapshotPreserveOptionalJoins() => InRollback(async (context, repository) =>
    {
        var supplier = await Supplier(context);
        var category = new ProductCategory { CategoryName = "随机分类", CategoryCode = Prefix() };
        await Add(context, category);
        var master = new ProductMaster { MasterSku = Prefix(), CategoryId = category.Uid };
        await Add(context, master);
        var product = await Product(context);
        product.ProductMasterId = master.Uid;
        product.DefaultSupplierId = supplier.Uid;
        var neutral = ProductNeutralReferenceTestData.Create(product, product.Sku + "当前关联码");
        await Add(context, neutral); product.ProductNeutralId = neutral.Uid; product.DescriptionEn = "当前英文"; product.DescriptionZh = "当前中文";
        product.FeaturesZh = "当前规格"; product.MaterialType = "当前角色"; product.MainImage = "仅存图片文本";
        Assert.IsTrue((await new PostgreSqlDapperRepository<Product>(context).UpdateAsync(product)).Success);
        var linked = Relation(supplier.Uid, product.Uid, Prefix());
        linked.UnitPrice = 0; linked.TaxPrice = 1.2345m;
        await Add(context, linked);
        var independent = Relation(supplier.Uid, null, Prefix());
        var missing = Relation(supplier.Uid, Guid.NewGuid(), Prefix());
        await Add(context, independent); await Add(context, missing);
        var dto = await repository.GetDtoByIdAsync(linked.Uid);
        Assert.IsNotNull(dto);
        Assert.AreEqual(supplier.SupplierCode, dto.SupplierCode);
        Assert.AreEqual(product.Sku, dto.ProductSku);
        Assert.AreEqual(neutral.ProductCode, dto.LinkSku);
        Assert.AreEqual("当前英文", dto.DescriptionEn);
        Assert.AreEqual("当前中文", dto.DescriptionZh);
        Assert.AreEqual("当前规格", dto.FeaturesZh);
        Assert.AreEqual("当前角色", dto.MaterialType);
        Assert.AreEqual("仅存图片文本", dto.MainImage);
        Assert.AreEqual("随机分类", dto.CategoryName);
        Assert.AreEqual(supplier.Currency, dto.Currency);
        Assert.AreEqual(supplier.TaxRate, dto.TaxRate);
        Assert.AreEqual(0m, dto.UnitPrice); Assert.AreEqual(1.2345m, dto.TaxPrice);
        Assert.AreEqual(linked.SupplierSkuDescriptionZh, dto.SupplierSkuDescriptionZh);
        Assert.AreEqual(linked.SupplierSkuFeaturesZh, dto.SupplierSkuFeaturesZh);
        Assert.IsTrue(dto.IsProductDefaultSupplier);
        var snapshot = (await repository.GetPurchaseDataAsync([(product.Uid, supplier.Uid)])).Single();
        Assert.AreEqual(linked.SupplierCode, snapshot.SupplierCode);
        Assert.AreEqual(linked.ProductSku, snapshot.ProductSku);
        Assert.AreEqual(linked.SupplierSku, snapshot.SupplierSku);
        Assert.AreNotEqual(snapshot.ProductSku, dto.ProductSku);
        foreach (var row in new[] { independent, missing })
        {
            var unlinked = await repository.GetDtoByIdAsync(row.Uid);
            Assert.IsNotNull(unlinked);
            Assert.AreEqual(row.SkuId, unlinked.SkuId);
            Assert.AreEqual("", unlinked.CategoryName);
            Assert.IsFalse(unlinked.IsProductDefaultSupplier);
        }
        Assert.IsTrue((await new PostgreSqlDapperRepository<ProductCategory>(context).DeleteAsync(category.Uid, 1)).Success);
        Assert.AreEqual("分类已失效", (await repository.GetDtoByIdAsync(linked.Uid))!.CategoryName);
        Assert.IsTrue((await new PostgreSqlDapperRepository<ProductMaster>(context).DeleteAsync(master.Uid, 1)).Success);
        Assert.AreEqual("未分类", (await repository.GetDtoByIdAsync(linked.Uid))!.CategoryName);
        Assert.IsTrue((await new PostgreSqlDapperRepository<Product>(context).DeleteAsync(product.Uid, 2)).Success);
        Assert.AreEqual("", (await repository.GetDtoByIdAsync(linked.Uid))!.CategoryName);
        Assert.HasCount(1, await repository.GetPurchaseDataAsync([(product.Uid, supplier.Uid)]), "采购快照入口不联表过滤 Product。");
        Assert.IsTrue((await new PostgreSqlDapperRepository<SwiftExport.Core.Entities.MasterData.Supplier>(context).DeleteAsync(supplier.Uid, 1)).Success);
        Assert.IsNull(await repository.GetDtoByIdAsync(linked.Uid));
    });

    /// <summary>供应商范围、默认页长/上限和稳定 UID 分页保持原规则；料号通配转义与停用供应商/产品范围不变。</summary>
    [TestMethod]
    public Task SupplierProduct_PagePreservesScopePatternAndStableIdentity() => InRollback(async (context, repository) =>
    {
        var supplier = await Supplier(context);
        var other = await Supplier(context);
        var prefix = Prefix();
        var rows = new[] { Relation(supplier.Uid, null, prefix + "~%_[\\ A1Z"), Relation(supplier.Uid, null, prefix + "~%_[\\ A1Z") };
        foreach (var row in rows) await Add(context, row);
        await Add(context, Relation(other.Uid, null, prefix));
        var deleted = Relation(supplier.Uid, null, prefix);
        await Add(context, deleted);
        Assert.IsTrue((await repository.DeleteAsync(deleted.Uid, 1)).Success);
        var request = new SwiftExport.Contracts.DTOs.Suppliers.Requests.SupplierProductSearchRequest
            { SupplierId = supplier.Uid, Keyword = prefix.ToLowerInvariant() + "~%_[\\ A?Z", PageIndex = 0, PageSize = 0 };
        var page = await repository.GetPagedAsync(request);
        Assert.AreEqual(1, page.PageIndex); Assert.AreEqual(20, page.PageSize); Assert.AreEqual(2, page.TotalCount);
        var expected = await context.Session.Connection.QueryAsync<Guid>(
            "SELECT uid FROM public.supplier_product WHERE supplier_id=@Id AND is_deleted=FALSE ORDER BY supplier_sku COLLATE public.erp_dictionary_ci,uid",
            new { Id = supplier.Uid }, context.Session.Transaction);
        CollectionAssert.AreEqual(expected.ToArray(), page.Items.Select(row => row.Uid).ToArray());
        request.PageSize = 1;
        var first = await repository.GetPagedAsync(request);
        request.PageIndex = 2;
        var second = await repository.GetPagedAsync(request);
        Assert.AreNotEqual(first.Items.Single().Uid, second.Items.Single().Uid);
        request.PageSize = 999;
        var capped = await repository.GetPagedAsync(request);
        Assert.AreEqual(200, capped.PageSize); Assert.AreEqual(2, capped.TotalCount); Assert.HasCount(0, capped.Items);
    });

    /// <summary>料号等值忽略大小写与尾部 ASCII 空格，前导空格不被暗 Trim；排除整批 UID 且空关联仍计入供应商引用。</summary>
    [TestMethod]
    public Task SupplierProduct_CodeScopesAndBatchExclusionPreserveOriginalText() => InRollback(async (context, repository) =>
    {
        var supplier = await Supplier(context); var other = await Supplier(context);
        var prefix = Prefix();
        var a = Relation(supplier.Uid, null, prefix + "Ab ");
        var b = Relation(supplier.Uid, null, prefix + "ab");
        var outside = Relation(other.Uid, null, prefix + "Ab");
        var deleted = Relation(supplier.Uid, null, prefix + "deleted");
        foreach (var row in new[] { a, b, outside, deleted }) await Add(context, row);
        Assert.IsTrue((await repository.DeleteAsync(deleted.Uid, 1)).Success);
        CollectionAssert.AreEquivalent(new[] { a.SupplierSku, b.SupplierSku },
            (await repository.GetExistingSupplierSkuListAsync([prefix.ToLowerInvariant() + "AB"], supplier.Uid)).ToArray());
        Assert.HasCount(0, await repository.GetExistingSupplierSkuListAsync([" " + prefix + "Ab"], supplier.Uid));
        Assert.HasCount(0, await repository.GetExistingSupplierSkuListAsync([deleted.SupplierSku], supplier.Uid));
        var occupied = await repository.GetSupplierSkusUsedByOtherRecordsAsync(supplier.Uid, [(Guid.NewGuid(), prefix + "AB")]);
        Assert.HasCount(1, occupied);
        Assert.IsTrue(new[] { a.SupplierSku, b.SupplierSku }.Contains(occupied.Single()));
        Assert.HasCount(0, await repository.GetSupplierSkusUsedByOtherRecordsAsync(supplier.Uid, [(a.Uid, prefix + "AB"), (b.Uid, prefix + "AB")]));
        CollectionAssert.AreEquivalent(new[] { supplier.Uid, other.Uid },
            (await repository.GetSupplierIdsWithProductsAsync([supplier.Uid, other.Uid, supplier.Uid, Guid.NewGuid()])).ToArray());
        Assert.HasCount(0, await repository.GetExistingSupplierSkuListAsync([], supplier.Uid));
        Assert.HasCount(0, await repository.GetSupplierSkusUsedByOtherRecordsAsync(supplier.Uid, []));
        Assert.HasCount(0, await repository.GetSupplierIdsWithProductsAsync([]));
    });

    /// <summary>采购超过 500 对键仍精确配对，不交叉命中；输入去重/滤空后保留全部请求匹配并按 CLR 身份排序。</summary>
    [TestMethod]
    public Task SupplierProduct_PurchaseChunkedPairsDoNotCrossMatch() => InRollback(async (context, repository) =>
    {
        var sa = await Supplier(context); var sb = await Supplier(context);
        var pa = await Product(context); var pb = await Product(context);
        var aa = Relation(sa.Uid, pa.Uid, Prefix()); var bb = Relation(sb.Uid, pb.Uid, Prefix());
        var ab = Relation(sb.Uid, pa.Uid, Prefix()); var ba = Relation(sa.Uid, pb.Uid, Prefix());
        foreach (var row in new[] { aa, bb, ab, ba }) await Add(context, row);
        var keys = new List<(Guid ProductId, Guid SupplierId)> { (pa.Uid, sa.Uid) };
        keys.AddRange(Enumerable.Range(0, 499).Select(_ => (Guid.NewGuid(), Guid.NewGuid())));
        keys.Add((pb.Uid, sb.Uid)); keys.Add(keys[0]); keys.Add((Guid.Empty, sa.Uid));
        var rows = await repository.GetPurchaseDataAsync(keys);
        CollectionAssert.AreEqual(new[] { aa, bb }.OrderBy(row => row.SkuId).ThenBy(row => row.SupplierId).ThenBy(row => row.Uid).Select(row => row.Uid).ToArray(),
            rows.Select(row => row.Uid).ToArray());
        Assert.HasCount(4, await repository.GetPurchaseDataAsync([(pa.Uid, sa.Uid), (pb.Uid, sb.Uid), (pa.Uid, sb.Uid), (pb.Uid, sa.Uid)]));
        Assert.HasCount(0, await repository.GetPurchaseDataAsync([]));
        Assert.IsTrue((await repository.DeleteAsync(aa.Uid, 1)).Success);
        Assert.HasCount(1, await repository.GetPurchaseDataAsync(keys));
    });

    /// <summary>继承 CRUD 保留 nullable SKU 和版本冲突；既有唯一约束是供应商加产品，碰撞不伪装成供应商料号占用。</summary>
    [TestMethod]
    public Task SupplierProduct_CrudAndCompositeConstraintKeepExistingSemantics() => InRollback(async (context, repository) =>
    {
        var supplier = await Supplier(context); var product = await Product(context);
        var row = Relation(supplier.Uid, product.Uid, Prefix());
        Assert.IsTrue((await repository.AddAsync(row)).Success);
        var current = await repository.GetByIdAsync(row.Uid, true);
        Assert.IsNotNull(current);
        Assert.AreEqual(row.SupplierCode, current.SupplierCode); Assert.AreEqual(row.ProductSku, current.ProductSku);
        Assert.IsNull(current.UnitPrice); Assert.IsNull(current.TaxPrice);
        var stale = await repository.GetByIdAsync(row.Uid);
        Assert.IsNotNull(stale);
        current.UnitPrice = 7.1234m;
        Assert.AreEqual(2, (await repository.UpdateAsync(current)).NewVersion);
        var conflict = await repository.UpdateAsync(stale);
        Assert.IsFalse(conflict.Success); Assert.AreEqual("CONCURRENCY_CONFLICT", conflict.ErrorCode);
        Assert.AreEqual(7.1234m, (await repository.GetByIdAsync(row.Uid))!.UnitPrice);
        await context.Session.Connection.ExecuteAsync("SAVEPOINT supplier_product_collision", transaction: context.Session.Transaction);
        try
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => repository.AddAsync(Relation(supplier.Uid, product.Uid, Prefix())));
            Assert.AreEqual("23505", error.SqlState);
            Assert.AreEqual("ux_supplier_product_supplier_product", error.ConstraintName);
            Assert.AreEqual("supplier_product", error.TableName);
        }
        finally
        {
            await context.Session.Connection.ExecuteAsync("ROLLBACK TO SAVEPOINT supplier_product_collision", transaction: context.Session.Transaction);
            await context.Session.Connection.ExecuteAsync("RELEASE SAVEPOINT supplier_product_collision", transaction: context.Session.Transaction);
        }
        var independent = Relation(supplier.Uid, null, row.SupplierSku);
        Assert.IsTrue((await repository.AddAsync(independent)).Success, "现有索引没有料号唯一，也不限制空 SKU 资料。");
        Assert.IsNull((await repository.GetByIdAsync(independent.Uid))!.SkuId);
        var deletion = await repository.DeleteAsync(row.Uid, 2);
        Assert.IsTrue(deletion.Success); Assert.AreEqual(2, deletion.NewVersion);
        Assert.IsNull(await repository.GetByIdAsync(row.Uid));
        Assert.IsTrue((await repository.AddAsync(Relation(supplier.Uid, product.Uid, Prefix()))).Success);
        Assert.IsNotNull(context.Session.Transaction);
    });

    /// <summary>只连接既有固定专用库，每例共用一个仓储会话事务，最终无条件回滚随机夹具。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, PostgreSqlSupplierProductRepository, Task> action)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        var user = new User();
        var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        try { await action(context, new PostgreSqlSupplierProductRepository(context)); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>通用仓储只插入本例随机实体，所有实体使用相同事务；不存在旧数据修改或运行注册。</summary>
    private static async Task Add<T>(RepositoryBaseContext context, T row) where T : SwiftExport.Core.Common.BaseEntity
    {
        var result = await new PostgreSqlDapperRepository<T>(context).AddAsync(row);
        Assert.IsTrue(result.Success, result.Message);
    }

    /// <summary>创建停用但未删除供应商，证明原读取只校验存在性而不新增活动过滤。</summary>
    private static async Task<Supplier> Supplier(RepositoryBaseContext context)
    {
        var row = new Supplier { SupplierCode = Prefix(), SupplierName = "随机供应商", DocRelateNo = Prefix(), IsActive = false, Currency = "CNY", TaxRate = 0.13m };
        await Add(context, row); return row;
    }

    /// <summary>创建停用但未删除产品用于资料联表，不把普通读取变成新业务准入。</summary>
    private static async Task<Product> Product(RepositoryBaseContext context)
    {
        var row = new Product { Sku = Prefix(), SupplyType = "Purchase", IsActive = false };
        await Add(context, row); return row;
    }

    /// <summary>编码快照故意不同于当前联表，独立关联合法且必填文本不置 NULL。</summary>
    private static SupplierProduct Relation(Guid supplier, Guid? product, string sku) => new()
    {
        SupplierId = supplier, SkuId = product, SupplierCode = "关系供应商快照", ProductSku = "关系产品快照", SupplierSku = sku,
        SupplierSkuDescriptionZh = "供应商自有描述", SupplierSkuFeaturesZh = "供应商自有规格"
    };

    /// <summary>生成短随机身份文本，不访问文件或更改原实体。</summary>
    private static string Prefix() => "PGSP" + Guid.NewGuid().ToString("N");

    /// <summary>给随机事务记录提供固定审计标记。</summary>
    private sealed class User : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_supplier_product__";
        public bool IsAuthenticated => false;
    }
}
