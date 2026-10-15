using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Products.Catalog;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>产品目录 PostgreSQL 专项验收，只在保留专用库内写随机记录并统一回滚，不部署结构。</summary>
[TestClass]
public sealed class PostgreSqlProductCatalogMigrationIntegrationTests
{
    /// <summary>当前客户代码来自真实三表关系；停用保留，多关系去重，删除、悬空关联及旧快照排除。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductCatalogIntegration")]
    public Task CustomerCodes_UseCurrentRelationshipsAndKeepInactive() => InRollback(async context =>
    {
        var prefix = Prefix();
        var product = ProductRow(prefix);
        product.IsActive = false;
        var deletedProduct = ProductRow(prefix + "D", true);
        await Insert(context, product);
        await Insert(context, deletedProduct);
        for (var i = 0; i < 7; i++)
        {
            var customer = new Customer { CustomerCode = prefix + i, CustomerName = prefix,
                DocRelateNo = prefix, IsActive = false, IsDeleted = i == 1 };
            await Insert(context, customer);
            var relation = new CustomerProduct { CustomerId = customer.Uid, CustomerCode = prefix + "Snapshot",
                CustomerSku = prefix + i, SkuId = i == 2 ? deletedProduct.Uid : i == 3 ? Guid.NewGuid() : i == 4 ? null : product.Uid,
                IsDeleted = i == 5, IsActive = false };
            if (i != 6) await Insert(context, relation);
            if (i == 0)
                await Insert(context, new CustomerProduct { CustomerId = customer.Uid, CustomerCode = prefix + "Old",
                    CustomerSku = prefix + "Other", SkuId = product.Uid });
        }
        SwiftExport.Core.Interfaces.Products.Catalog.IProductRepository repository = new PostgreSqlProductRepository(context);
        var result = (await repository.GetAllCustomerCodeListAsync()).Where(code => code.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        CollectionAssert.AreEqual(new[] { prefix + "0" }, result);
    });

    /// <summary>编码等值保留尾部空格与忽略大小写；前导空格、删除和停用边界按各入口分别保留。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductCatalogIntegration")]
    public Task CodesAndBom_KeepInputAndLifecycleBoundaries() => InRollback(async context =>
    {
        var prefix = Prefix();
        var product = ProductRow(prefix + "Ab ");
        product.IsActive = false;
        product.BomNo = prefix + "Bom ";
        var leading = ProductRow(" " + prefix + "Lead");
        var deleted = ProductRow(prefix + "Deleted", true);
        foreach (var row in new[] { product, leading, deleted }) await Insert(context, row);
        var repository = new PostgreSqlProductRepository(context);
        var mapped = await repository.GetBySkuAsync(prefix.ToLowerInvariant() + "ab");
        Assert.AreEqual(product.Uid, mapped!.Uid);
        Assert.IsFalse(mapped.IsActive);
        Assert.AreEqual(product.Sku, mapped.Sku);
        Assert.IsNull(await repository.GetBySkuAsync(prefix + "Lead"));
        Assert.AreEqual(leading.Uid, (await repository.GetBySkuAsync(leading.Sku))!.Uid);
        Assert.IsNull(await repository.GetBySkuAsync(deleted.Sku));
        Assert.AreEqual(product.Uid, (await repository.GetByBomNoAsync(" " + prefix.ToLowerInvariant() + "bom "))!.Uid);
        Assert.IsNull(await repository.GetByBomNoAsync(" "));
        Assert.AreEqual(product.Uid, (await repository.GetBySkuListAsync([" " + prefix.ToLowerInvariant() + "ab ", prefix + "AB", deleted.Sku])).Single().Uid);
        CollectionAssert.AreEquivalent(new[] { product.Sku, deleted.Sku },
            (await repository.GetExistingSkuListAsync([prefix.ToLowerInvariant() + "ab", deleted.Sku])).ToArray());
        Assert.HasCount(0, await repository.GetExistingSkuListAsync([]));
    });

    /// <summary>详情实时分类与名称遵守软删，保留失效标识和 NULL；分页包装仅取外箱，不受其他类型装率干扰。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductCatalogIntegration")]
    public Task DetailsAndPage_MapNullableReferencesAndPackaging() => InRollback(async context =>
    {
        var prefix = Prefix();
        var category = new ProductCategory { CategoryName = prefix + "Category" };
        var master = new ProductMaster { MasterSku = prefix, CategoryId = category.Uid };
        var supplier = new Supplier { SupplierCode = prefix, SupplierName = "停用仍展示", DocRelateNo = prefix, IsActive = false };
        var deletedSupplier = new Supplier { SupplierCode = prefix + "D", SupplierName = "删除隐藏", DocRelateNo = prefix, IsDeleted = true };
        var color = new ProductColor { ColorCodeUnique = prefix, ColorCode = "非唯一颜色", ColorNameZh = "饰面" };
        foreach (var row in new BaseEntity[] { category, master, supplier, deletedSupplier, color }) await Insert(context, row);
        var product = ProductRow(prefix + "A");
        product.ProductMasterId = master.Uid;
        product.DefaultSupplierId = supplier.Uid;
        product.ManufacturerId = deletedSupplier.Uid;
        product.DefaultWarehouseSiteId = Guid.NewGuid();
        product.ColorId = color.Uid;
        product.MaterialType = "Metal";
        var noReferences = ProductRow(prefix + "B");
        noReferences.ProductMasterId = Guid.NewGuid();
        foreach (var row in new[] { product, noReferences }) await Insert(context, row);
        var chosen = new ProductPackage { OwnerProductId = product.Uid,
            PackageType = SwiftExport.Contracts.SysEnums.ProductPackageTypeCodes.Carton, PcsPerPackage = 5, Gw_KG = 6m, Cbm = 0.2m };
        var samePriority = new ProductPackage { OwnerProductId = product.Uid, PackageType = "InnerBox", PcsPerPackage = 100, Gw_KG = 7m };
        var ordinary = new ProductPackage { OwnerProductId = product.Uid, PackageType = "Pallet", PcsPerPackage = 99 };
        var hidden = new ProductPackage { OwnerProductId = product.Uid, PcsPerPackage = 999, IsDeleted = true };
        foreach (var row in new[] { chosen, samePriority, ordinary, hidden }) await Insert(context, row);
        var repository = new PostgreSqlProductRepository(context);
        var idDetail = await repository.GetDtoByIdAsync(product.Uid);
        Assert.AreEqual(deletedSupplier.Uid, idDetail!.ManufacturerId);
        Assert.IsNull(idDetail.ManufacturerName);
        Assert.IsNull(idDetail.DefaultWarehouseName);
        Assert.AreEqual(supplier.SupplierName, idDetail.SupplierName);
        var skuDetail = await repository.GetDtoBySkuAsync(prefix.ToLowerInvariant() + "a");
        Assert.AreEqual(category.Uid, skuDetail!.CategoryId);
        Assert.AreEqual(category.CategoryName, skuDetail.CategoryName);
        Assert.AreEqual("饰面", skuDetail.Finish);
        Assert.AreEqual(0m, skuDetail.USDPrice);
        var page = await repository.GetPagedAsync(new() { Keyword = prefix });
        Assert.AreEqual(2, page.TotalCount);
        var projected = page.Items.Single(row => row.Uid == product.Uid);
        Assert.AreEqual(5m, projected.PcsOfCarton);
        Assert.AreEqual(chosen.Gw_KG, projected.GwOfCarton);
        Assert.AreEqual("未分类", page.Items.Single(row => row.Uid == noReferences.Uid).CategoryName);
        Assert.IsNull(page.Items.Single(row => row.Uid == noReferences.Uid).PcsOfCarton);
        Assert.AreEqual(noReferences.Uid, (await repository.GetPagedAsync(new() { Keyword = prefix, PageSize = 1, PageIndex = 2 })).Items.Single().Uid);
        var combined = await repository.GetPagedAsync(new() { Keyword = prefix, CategoryId = category.Uid,
            CategoryIds = [category.Uid], ColorIds = [color.Uid], ColorCodeUnique = prefix.ToLowerInvariant(),
            MaterialTypeList = [" metal "], CategoryName = prefix.ToLowerInvariant(), BatchKeywords = [product.Sku.ToLowerInvariant()] });
        Assert.AreEqual(1, combined.TotalCount);
        Assert.AreEqual(product.Uid, combined.Items.Single().Uid);
        Assert.AreEqual(0, (await repository.GetPagedAsync(new() { Keyword = prefix, CategoryId = category.Uid, CategoryIds = [Guid.NewGuid()] })).TotalCount);
        var literal = ProductRow(prefix + "~%_[\\ X1Z");
        await Insert(context, literal);
        Assert.AreEqual(literal.Uid, (await repository.GetPagedAsync(new() { Keyword = prefix + "~%_[\\ X?Z" })).Items.Single().Uid);
    });

    /// <summary>客户代码取当前 Customer 而非关系快照，独立客户筛选保留 AND，重复关系不扩大分页。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductCatalogIntegration")]
    public Task CustomerFilters_UseCurrentCodesWithoutDuplicateProducts() => InRollback(async context =>
    {
        var prefix = Prefix();
        var customer = new Customer { CustomerCode = prefix + "Current", DocRelateNo = prefix, IsActive = false };
        var product = ProductRow(prefix);
        var hidden = ProductRow(prefix + "Hidden", true);
        foreach (var row in new BaseEntity[] { customer, product, hidden }) await Insert(context, row);
        foreach (var row in new[] { product, product, hidden }) await Insert(context, new CustomerProduct
        { CustomerId = customer.Uid, CustomerCode = prefix + "Old", SkuId = row.Uid, CustomerSku = Prefix(), IsActive = false });
        var repository = new PostgreSqlProductRepository(context);
        var page = await repository.GetPagedAsync(new() { Keyword = prefix, CustomerId = customer.Uid,
            CustomerIds = [customer.Uid], CustomerCodes = [" " + customer.CustomerCode.ToLowerInvariant() + " "] });
        Assert.AreEqual(1, page.TotalCount);
        Assert.AreEqual(product.Uid, page.Items.Single().Uid);
        Assert.AreEqual(0, (await repository.GetPagedAsync(new() { Keyword = prefix, CustomerCodes = [prefix + "Old"] })).TotalCount);
        Assert.AreEqual(0, (await repository.GetPagedAsync(new() { Keyword = prefix, CustomerId = customer.Uid, CustomerIds = [Guid.NewGuid()] })).TotalCount);
    });

    /// <summary>候选排序保持四级优先及关系 EXISTS；裸模式允许百分号下划线，反斜杠仍按字面匹配。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductCatalogIntegration")]
    public Task Lookup_PreservesRankWildcardsAndLiteralBackslash() => InRollback(async context =>
    {
        var prefix = Prefix();
        var exact = ProductRow(prefix);
        var customerExact = ProductRow(prefix + "Z1");
        var productPrefix = ProductRow(prefix + "A");
        var customerPrefix = ProductRow(prefix + "Z2");
        var contains = ProductRow("X" + prefix);
        var slash = ProductRow(prefix + "\\X");
        foreach (var row in new[] { exact, customerExact, productPrefix, customerPrefix, contains, slash }) await Insert(context, row);
        foreach (var code in new[] { prefix, prefix }) await Insert(context, new CustomerProduct
        { CustomerId = Guid.NewGuid(), CustomerCode = prefix, SkuId = customerExact.Uid, CustomerSku = code });
        await Insert(context, new CustomerProduct { CustomerId = Guid.NewGuid(), CustomerCode = prefix,
            SkuId = customerPrefix.Uid, CustomerSku = prefix + "Suffix", IsActive = false });
        // 使关系候选产品 SKU 不参与产品前缀排名，唯有 CustomerSku 提供命中。
        await context.Session.Connection.ExecuteAsync("UPDATE public.product SET sku=@Sku WHERE uid=@Uid",
            new { Sku = "Z" + prefix + "Z2", Uid = customerPrefix.Uid }, context.Session.Transaction);
        var repository = new PostgreSqlProductRepository(context);
        var candidates = await repository.SearchLookupAsync(new() { Keyword = prefix });
        Assert.AreEqual(exact.Uid, candidates[0].Uid);
        Assert.AreEqual(customerExact.Uid, candidates[1].Uid);
        Assert.AreEqual(1, candidates.Count(row => row.Uid == customerExact.Uid));
        Assert.IsTrue(candidates.ToList().FindIndex(row => row.Uid == productPrefix.Uid) < candidates.ToList().FindIndex(row => row.Uid == customerPrefix.Uid));
        Assert.IsTrue(candidates.ToList().FindIndex(row => row.Uid == customerPrefix.Uid) < candidates.ToList().FindIndex(row => row.Uid == contains.Uid));
        Assert.AreEqual(slash.Uid, (await repository.SearchLookupAsync(new() { Keyword = prefix + "\\X" })).Single().Uid);
        Assert.HasCount(6, await repository.SearchLookupAsync(new() { Keyword = prefix + "%", Limit = 999 }));
        Assert.IsFalse((await repository.SearchLookupAsync(new() { Keyword = prefix, ExcludeProductId = exact.Uid })).Any(row => row.Uid == exact.Uid));
    });

    /// <summary>普通 CRUD 跳过启停，版本递增与旧版本冲突继续由通用仓储处理。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductCatalogIntegration")]
    public Task Crud_DoesNotGrantLifecycleWritePermission() => InRollback(async context =>
    {
        var product = ProductRow(Prefix());
        product.IsActive = false;
        var repository = new PostgreSqlProductRepository(context);
        Assert.IsTrue((await repository.AddAsync(product)).Success);
        product.IsActive = true;
        product.DescriptionZh = "普通资料更新";
        Assert.IsTrue((await repository.UpdateAsync(product)).Success);
        var saved = await repository.GetByIdAsync(product.Uid);
        Assert.IsFalse(saved!.IsActive);
        Assert.AreEqual(2, saved.Version);
        Assert.AreEqual("普通资料更新", saved.DescriptionZh);
        product.Version = 1;
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.UpdateAsync(product)).ErrorCode);
        Assert.IsTrue((await repository.DeleteAsync(product.Uid, 2)).Success);
        Assert.IsNull(await repository.GetBySkuAsync(product.Sku));
        Assert.IsNull(await repository.GetDtoByIdAsync(product.Uid));
        Assert.IsNull(await repository.GetDtoBySkuAsync(product.Sku));
        Assert.AreEqual(product.Sku, (await repository.GetExistingSkuListAsync([product.Sku])).Single());
    });

    /// <summary>每个场景在保留专用库的调用方事务中执行，成功或异常都回滚随机数据。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, Task> scenario)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        var user = new TestUser();
        var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        try { await scenario(context); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>通过真实实体映射插入随机全字段记录，审计时间使用已有提供者参数绑定。</summary>
    private static async Task Insert(RepositoryBaseContext context, BaseEntity entity)
    {
        context.AuditStamp.StampForInsert(entity);
        var metadata = EntityMetadataCache.GetMetadata(entity.GetType());
        var columns = string.Join(",", metadata.Columns.Select(column => PostgreSqlNames.Quote(column.ColumnName)));
        var values = string.Join(",", metadata.Columns.Select(column => "@" + column.ParameterName));
        Assert.AreEqual(1, await context.Session.Connection.ExecuteAsync(
            $"INSERT INTO public.{PostgreSqlNames.Quote(metadata.TableName)} ({columns}) VALUES ({values})",
            PostgreSqlParameters.Entity(entity, metadata), context.Session.Transaction));
    }

    /// <summary>随机产品满足基础必填字段，不使用现存主数据作为可写夹具。</summary>
    private static Product ProductRow(string sku, bool deleted = false) => new()
    { Uid = Guid.NewGuid(), Sku = sku, SupplyType = "Purchase", IsDeleted = deleted, Version = 1 };

    /// <summary>短随机编码隔离 LIKE 与唯一索引验收数据。</summary>
    private static string Prefix() => "PGC" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>测试身份仅提供审计名称，不模拟业务授权。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_catalog__";
        public bool IsAuthenticated => false;
    }
}
