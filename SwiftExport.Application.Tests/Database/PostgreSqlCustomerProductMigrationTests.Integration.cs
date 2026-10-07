using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Customers.CustomerProducts;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>客户产品实库验收使用保留专用库，所有实体随机创建并在同事务回滚，不提交或修改已有记录。</summary>
[TestClass]
public sealed class PostgreSqlCustomerProductMigrationIntegrationTests
{
    /// <summary>实时客户码与关系 SKU 快照区别明确；可选产品关联、NULL 价格和空串描述按原投影保留。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlCustomerProductIntegration")]
    public Task Detail_PreservesNonstandardRelationsSnapshotsAndNullableValues() => InRollback(async context =>
    {
        var prefix = Prefix();
        var customer = CustomerRow(prefix);
        customer.IsActive = false;
        var category = new ProductCategory { CategoryName = prefix + "Category" };
        var master = new ProductMaster { MasterSku = prefix, CategoryId = category.Uid };
        var product = new Product { Sku = prefix + "CurrentProduct", SupplyType = "Purchase", DescriptionEn = "", ProductMasterId = master.Uid,
            MaterialType = "Metal", MainImage = "product-image", IsActive = false };
        foreach (var entity in new BaseEntity[] { customer, category, master, product }) await Insert(context, entity);
        var linked = Relation(customer.Uid, prefix + "Linked", product.Uid);
        linked.IsActive = false;
        linked.CustomerCode = "old-customer";
        linked.Sku = "snapshot-product";
        linked.LinkSku = "snapshot-link";
        linked.DescriptionEn = "fallback-description";
        linked.FeaturesEn = "relation-features";
        var nonstandard = Relation(customer.Uid, prefix + "NoProduct");
        nonstandard.DescriptionEn = "nonstandard-description";
        var missingProduct = Relation(customer.Uid, prefix + "Missing", Guid.NewGuid());
        missingProduct.DescriptionEn = "missing-product-description";
        foreach (var entity in new[] { linked, nonstandard, missingProduct }) await Insert(context, entity);
        var repository = new PostgreSqlCustomerProductRepository(context);
        var dto = await repository.GetDtoByIdAsync(linked.Uid);
        Assert.AreEqual(customer.CustomerCode, dto!.CustomerCode);
        Assert.AreEqual("snapshot-product", dto.Sku);
        Assert.AreEqual("snapshot-link", dto.LinkSku);
        Assert.AreEqual("", dto.Description, "有效空串不能被关系描述覆盖。");
        Assert.AreEqual("fallback-description", dto.DescriptionEn);
        Assert.AreEqual("relation-features", dto.FeaturesEn);
        Assert.AreEqual(category.CategoryName, dto.CategoryName);
        Assert.AreEqual("Metal", dto.MaterialType);
        Assert.AreEqual("product-image", dto.MainImage);
        Assert.IsNull(dto.USDPrice);
        Assert.IsNull(dto.CNYPrice);
        Assert.IsFalse(dto.IsActive);
        var unlinked = await repository.GetDtoByIdAsync(nonstandard.Uid);
        Assert.IsNull(unlinked!.SkuId);
        Assert.AreEqual("nonstandard-description", unlinked.Description);
        Assert.AreEqual("未分类", unlinked.CategoryName);
        Assert.AreEqual("missing-product-description", (await repository.GetDtoByIdAsync(missingProduct.Uid))!.Description);
        var page = await repository.GetPagedAsync(new() { CustomerId = customer.Uid });
        Assert.AreEqual(3, page.TotalCount);
        Assert.HasCount(3, page.Items);
    });

    /// <summary>产品、主型号或分类失效只隐藏相关展示；客户失效隐藏 DTO，但实体及关联客户身份查询仍保留。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlCustomerProductIntegration")]
    public Task SoftDeletionAndMissingJoins_KeepEachQueryBoundary() => InRollback(async context =>
    {
        var prefix = Prefix();
        var customer = CustomerRow(prefix);
        var deletedCustomer = CustomerRow(prefix + "Deleted", true);
        var deletedProduct = new Product { Sku = prefix + "DeletedProduct", SupplyType = "Purchase", IsDeleted = true };
        var deletedMaster = new ProductMaster { MasterSku = prefix + "DeletedMaster", IsDeleted = true };
        var deletedCategory = new ProductCategory { CategoryName = prefix + "DeletedCategory", IsDeleted = true };
        var master = new ProductMaster { MasterSku = prefix, CategoryId = deletedCategory.Uid };
        var invalidCategoryProduct = new Product { Sku = prefix + "BadCategory", SupplyType = "Purchase", ProductMasterId = master.Uid };
        var invalidMasterProduct = new Product { Sku = prefix + "BadMaster", SupplyType = "Purchase", ProductMasterId = deletedMaster.Uid };
        foreach (var entity in new BaseEntity[] { customer, deletedCustomer, deletedProduct, deletedMaster, deletedCategory, master, invalidCategoryProduct, invalidMasterProduct })
            await Insert(context, entity);
        var deletedProductRelation = Relation(customer.Uid, prefix + "DeletedProduct", deletedProduct.Uid);
        deletedProductRelation.DescriptionEn = "relation-description";
        var invalidCategory = Relation(customer.Uid, prefix + "BadCategory", invalidCategoryProduct.Uid);
        var invalidMaster = Relation(customer.Uid, prefix + "BadMaster", invalidMasterProduct.Uid);
        var hiddenCustomer = Relation(deletedCustomer.Uid, prefix + "DeletedCustomer");
        var orphan = Relation(Guid.NewGuid(), prefix + "Orphan");
        var deletedRelation = Relation(customer.Uid, prefix + "DeletedRelation");
        deletedRelation.IsDeleted = true;
        foreach (var entity in new[] { deletedProductRelation, invalidCategory, invalidMaster, hiddenCustomer, orphan, deletedRelation }) await Insert(context, entity);
        var repository = new PostgreSqlCustomerProductRepository(context);
        var productMissing = await repository.GetDtoByIdAsync(deletedProductRelation.Uid);
        Assert.AreEqual(deletedProduct.Uid, productMissing!.SkuId);
        Assert.AreEqual("relation-description", productMissing.Description);
        Assert.AreEqual("未分类", productMissing.CategoryName);
        Assert.AreEqual("分类已失效", (await repository.GetDtoByIdAsync(invalidCategory.Uid))!.CategoryName);
        Assert.AreEqual("未分类", (await repository.GetDtoByIdAsync(invalidMaster.Uid))!.CategoryName);
        Assert.IsNull(await repository.GetDtoByIdAsync(hiddenCustomer.Uid));
        Assert.IsNull(await repository.GetDtoByIdAsync(orphan.Uid));
        Assert.IsNull(await repository.GetDtoByIdAsync(deletedRelation.Uid));
        Assert.AreEqual(hiddenCustomer.Uid, (await repository.GetByCustomerSkuAsync(hiddenCustomer.CustomerSku, deletedCustomer.Uid))!.Uid);
        Assert.IsNull(await repository.GetByCustomerSkuAsync(deletedRelation.CustomerSku, customer.Uid));
        var ids = await repository.GetCustomerIdsWithProductsAsync([customer.Uid, deletedCustomer.Uid, orphan.CustomerId, Guid.NewGuid()]);
        CollectionAssert.AreEquivalent(new[] { customer.Uid, deletedCustomer.Uid, orphan.CustomerId }, ids.ToArray());
        var all = await repository.GetAllCustomerIdListAsync();
        Assert.IsTrue(ids.All(all.Contains));
        Assert.AreEqual(3, (await repository.GetPagedAsync(new() { Keyword = prefix })).TotalCount);
    });

    /// <summary>现有原文本索引允许同客户大小写或尾空格等价码并存；占用查询合组返回原文，现存查询仍返回全部。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlCustomerProductIntegration")]
    public Task CodesAndExclusions_DocumentRawIndexAndQueryEquivalence() => InRollback(async context =>
    {
        var prefix = Prefix();
        var customer = CustomerRow(prefix);
        var otherCustomer = CustomerRow(prefix + "Other");
        await Insert(context, customer);
        await Insert(context, otherCustomer);
        var first = Relation(customer.Uid, prefix + "Ab ");
        var equivalent = Relation(customer.Uid, prefix.ToLowerInvariant() + "ab");
        var leading = Relation(customer.Uid, " " + prefix + "Lead");
        var otherScope = Relation(otherCustomer.Uid, first.CustomerSku);
        var deleted = Relation(customer.Uid, prefix + "Deleted");
        deleted.IsDeleted = true;
        foreach (var entity in new[] { first, equivalent, leading, otherScope, deleted }) await Insert(context, entity);
        var repository = new PostgreSqlCustomerProductRepository(context);
        CollectionAssert.AreEquivalent(new[] { first.CustomerSku, equivalent.CustomerSku },
            (await repository.GetExistingCustomerSkuListAsync([prefix + "AB", prefix + "AB"], customer.Uid)).ToArray());
        var occupied = await repository.GetCustomerSkusUsedByOtherRecordsAsync(customer.Uid, [(Guid.NewGuid(), prefix + "AB")]);
        Assert.HasCount(1, occupied);
        Assert.IsTrue(new[] { first.CustomerSku, equivalent.CustomerSku }.Contains(occupied.Single()), "必须保留真实存储码。");
        var expectedRepresentative = await context.Session.Connection.ExecuteScalarAsync<string>(
            "SELECT customer_sku FROM public.customer_product WHERE uid=ANY(@Ids) ORDER BY uid LIMIT 1",
            new { Ids = new[] { first.Uid, equivalent.Uid } }, context.Session.Transaction);
        Assert.AreEqual(expectedRepresentative, occupied.Single());
        Assert.HasCount(0, await repository.GetCustomerSkusUsedByOtherRecordsAsync(customer.Uid,
            [(first.Uid, equivalent.CustomerSku), (equivalent.Uid, first.CustomerSku)]));
        Assert.AreEqual(equivalent.CustomerSku, (await repository.GetCustomerSkusUsedByOtherRecordsAsync(customer.Uid, [(first.Uid, first.CustomerSku)])).Single());
        Assert.HasCount(0, await repository.GetExistingCustomerSkuListAsync([prefix + "Lead", deleted.CustomerSku], customer.Uid));
        Assert.AreEqual(leading.Uid, (await repository.GetByCustomerSkuAsync(leading.CustomerSku, customer.Uid))!.Uid);
        Assert.AreEqual(otherScope.Uid, (await repository.GetByCustomerSkuAsync(first.CustomerSku, otherCustomer.Uid))!.Uid);
        // 只在随机场景使用 savepoint 恢复预期约束失败，不吞其他 SQLSTATE 或创建仓储隐式事务。
        await context.Session.Connection.ExecuteAsync("SAVEPOINT customer_product_exact_duplicate", transaction: context.Session.Transaction);
        var exception = await Assert.ThrowsExactlyAsync<PostgresException>(() => Insert(context, Relation(customer.Uid, first.CustomerSku)));
        Assert.AreEqual("23505", exception.SqlState);
        Assert.AreEqual("customer_product", exception.TableName);
        Assert.AreEqual("ux_customer_product_customer_customer_sku", exception.ConstraintName);
        await context.Session.Connection.ExecuteAsync("ROLLBACK TO SAVEPOINT customer_product_exact_duplicate", transaction: context.Session.Transaction);
        Assert.AreEqual(1, await context.Session.Connection.ExecuteScalarAsync<int>("SELECT 1", transaction: context.Session.Transaction));
    });

    /// <summary>分页五字段与转义匹配、稳定同码跨客户 UID 排序正确；继承 CRUD 保留启停可写资格、版本及软删。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlCustomerProductIntegration")]
    public Task PageAndCrud_PreserveLimitsSearchAndVersion() => InRollback(async context =>
    {
        var prefix = Prefix();
        var customer = CustomerRow(prefix);
        var secondCustomer = CustomerRow(prefix + "Other");
        await Insert(context, customer);
        await Insert(context, secondCustomer);
        var first = Relation(customer.Uid, prefix);
        var second = Relation(secondCustomer.Uid, prefix);
        foreach (var entity in new[] { first, second }) await Insert(context, entity);
        var repository = new PostgreSqlCustomerProductRepository(context);
        var page = await repository.GetPagedAsync(new() { Keyword = prefix.ToLowerInvariant(), PageIndex = 0, PageSize = 999 });
        Assert.AreEqual(2, page.TotalCount);
        Assert.AreEqual(1, page.PageIndex);
        Assert.AreEqual(200, page.PageSize);
        var sorted = page.Items.Select(row => row.Uid).ToArray();
        CollectionAssert.AreEqual(sorted.Order().ToArray(), sorted);
        Assert.AreEqual(sorted[1], (await repository.GetPagedAsync(new() { Keyword = prefix, PageIndex = 2, PageSize = 1 })).Items.Single().Uid);
        Assert.HasCount(0, (await repository.GetPagedAsync(new() { Keyword = prefix, PageIndex = 3, PageSize = 1 })).Items);
        Assert.AreEqual(20, (await repository.GetPagedAsync(new() { CustomerId = customer.Uid, PageSize = 0 })).PageSize);
        Assert.AreEqual(first.Uid, (await repository.GetPagedAsync(new() { CustomerId = customer.Uid, CustomerIds = [secondCustomer.Uid], CategoryId = Guid.NewGuid() })).Items.Single().Uid);
        var literal = prefix + "~%_[\\ X1Z";
        var search = Relation(customer.Uid, prefix + "Search");
        search.Sku = literal;
        search.LinkSku = literal;
        search.CustomerPdDescription = literal;
        search.CustomerPdSeries = literal;
        Assert.IsTrue((await repository.AddAsync(search)).Success);
        Assert.AreEqual(search.Uid, (await repository.GetPagedAsync(new() { Keyword = prefix + "~%_[\\ X?Z" })).Items.Single().Uid);
        foreach (var property in new[] { nameof(CustomerProduct.CustomerSku), nameof(CustomerProduct.Sku), nameof(CustomerProduct.LinkSku), nameof(CustomerProduct.CustomerPdDescription), nameof(CustomerProduct.CustomerPdSeries) })
        {
            var uniqueKeyword = Prefix();
            var row = Relation(customer.Uid, Prefix());
            typeof(CustomerProduct).GetProperty(property)!.SetValue(row, uniqueKeyword);
            await Insert(context, row);
            Assert.AreEqual(row.Uid, (await repository.GetPagedAsync(new() { Keyword = uniqueKeyword.ToLowerInvariant() })).Items.Single().Uid);
        }
        search.IsActive = false;
        search.Note = "资料更新允许既有启停字段";
        Assert.IsTrue((await repository.UpdateAsync(search)).Success);
        var saved = await repository.GetByIdAsync(search.Uid);
        Assert.AreEqual(2, saved!.Version);
        Assert.IsFalse(saved.IsActive);
        Assert.AreEqual(search.Note, saved.Note);
        search.Version = 1;
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.UpdateAsync(search)).ErrorCode);
        Assert.IsTrue((await repository.DeleteAsync(search.Uid, 2)).Success);
        Assert.IsNull(await repository.GetDtoByIdAsync(search.Uid));
        Assert.HasCount(0, await repository.GetExistingCustomerSkuListAsync([search.CustomerSku], customer.Uid));
        Assert.HasCount(0, await repository.GetCustomerSkusUsedByOtherRecordsAsync(customer.Uid, []));
        Assert.HasCount(0, await repository.GetCustomerIdsWithProductsAsync([]));
    });

    /// <summary>每个场景只在固定保留专用库写入随机事务数据，最终统一回滚，禁止提交夹具。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, Task> scenario)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        var user = new TestUser();
        try { await scenario(new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user)); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>依据真实实体元数据与既有时间参数规则插入全字段随机行，拒绝依赖默认值隐藏映射问题。</summary>
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

    /// <summary>随机客户保持必填编码及资料编号。</summary>
    private static Customer CustomerRow(string code, bool deleted = false) => new()
    { Uid = Guid.NewGuid(), CustomerCode = code, DocRelateNo = code, IsDeleted = deleted };

    /// <summary>随机客户产品保留关系快照和可选产品身份，无自动创建产品。</summary>
    private static CustomerProduct Relation(Guid customer, string code, Guid? product = null) => new()
    { Uid = Guid.NewGuid(), CustomerId = customer, CustomerCode = "snapshot", CustomerSku = code, SkuId = product };

    /// <summary>短随机编码隔离查询及唯一约束场景。</summary>
    private static string Prefix() => "PGCP" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>仅提供测试审计身份，不改变业务权限。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_customer_product__";
        public bool IsAuthenticated => false;
    }
}
