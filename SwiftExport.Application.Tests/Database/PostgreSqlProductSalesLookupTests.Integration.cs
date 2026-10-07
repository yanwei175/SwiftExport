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

/// <summary>销售产品选择的真实投影和客户覆盖验收，仅在保留专用库插入随机记录并回滚。</summary>
[TestClass]
public sealed class PostgreSqlProductSalesLookupIntegrationTests
{
    /// <summary>普通选择保留停用产品、可空价格和显示默认值，关键词大小写及通配转义沿用原规则。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductCatalogIntegration")]
    public Task PlainLookup_PreservesProjectionPatternAndPagination() => Rollback(async context =>
    {
        var prefix = Prefix();
        var code = prefix + "~%_[\\ A1Z";
        var product = Product(code);
        product.IsActive = false;
        await Insert(context, product);
        await Insert(context, Product(code + "gone", deleted: true));
        var repository = new PostgreSqlProductRepository(context);
        var result = await repository.SearchSalesOrderProductsAsync(new()
            { Keyword = prefix.ToLowerInvariant() + "~%_[\\ A?Z", PageIndex = 0, PageSize = 0 });
        Assert.AreEqual(1, result.TotalCount);
        Assert.AreEqual(1, result.PageIndex);
        Assert.AreEqual(30, result.PageSize);
        var row = result.Items.Single();
        Assert.AreEqual(product.Uid, row.ProductId);
        Assert.IsNull(row.CustomerProductId);
        Assert.AreEqual("", row.CustomerSku);
        Assert.AreEqual("", row.LinkSku);
        Assert.AreEqual("", row.ColorNameEn);
        Assert.AreEqual("", row.FeaturesEn);
        Assert.IsNull(row.CnyPrice);
        Assert.IsNull(row.UsdPrice);
        var empty = await repository.SearchSalesOrderProductsAsync(new() { Keyword = prefix, PageIndex = 2, PageSize = 200 });
        Assert.AreEqual(100, empty.PageSize);
        Assert.AreEqual(1, empty.TotalCount);
        Assert.AreEqual(0, empty.Items.Count);
    });

    /// <summary>客户关系必须有效；零价及空文本是明确覆盖，NULL才回退，不把关系独立档案作为产品候选。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductCatalogIntegration")]
    public Task CustomerLookup_PreservesZeroNullAndRelationshipScope() => Rollback(async context =>
    {
        var prefix = Prefix();
        var product = Product(prefix);
        product.IsActive = false;
        product.CNYPrice = 12;
        product.USDPrice = 3;
        product.LinkSku = "fallback";
        product.DescriptionEn = "description";
        var color = new ProductColor { ColorCodeUnique = Prefix(), ColorCode = Prefix(), ColorNameEn = "blue" };
        await Insert(context, color);
        product.ColorId = color.Uid;
        await Insert(context, product);
        var customer = new Customer { CustomerCode = Prefix(), CustomerName = "测试客户", DocRelateNo = Prefix() };
        await Insert(context, customer);
        var first = Relation(customer.Uid, product.Uid, prefix + "A");
        first.CNYPrice = 0; first.LinkSku = "";
        var second = Relation(customer.Uid, product.Uid, prefix + "B");
        var inactive = Relation(customer.Uid, product.Uid, prefix + "C"); inactive.IsActive = false;
        var deleted = Relation(customer.Uid, product.Uid, prefix + "D"); deleted.IsDeleted = true;
        var independent = Relation(customer.Uid, null, prefix + "E");
        foreach (var relation in new[] { first, second, inactive, deleted, independent }) await Insert(context, relation);
        var repository = new PostgreSqlProductRepository(context);
        var result = await repository.SearchSalesOrderProductsAsync(new() { CustomerId = customer.Uid, Keyword = prefix.ToLowerInvariant() });
        Assert.AreEqual(2, result.TotalCount);
        var overridden = result.Items.Single(row => row.CustomerProductId == first.Uid);
        Assert.AreEqual(0m, overridden.CnyPrice);
        Assert.AreEqual(3m, overridden.UsdPrice);
        Assert.AreEqual("", overridden.LinkSku);
        Assert.AreEqual("blue", overridden.ColorNameEn);
        // 原 SQL 投影名 DefaultDescriptionEn 与 DTO DescriptionEn 不一致；本批迁移不暗改公共输出。
        Assert.IsNull(overridden.DescriptionEn);
        Assert.AreEqual("fallback", result.Items.Single(row => row.CustomerProductId == second.Uid).LinkSku);
        var firstPage = await repository.SearchSalesOrderProductsAsync(new() { CustomerId = customer.Uid, PageSize = 1 });
        var secondPage = await repository.SearchSalesOrderProductsAsync(new() { CustomerId = customer.Uid, PageSize = 1, PageIndex = 2 });
        Assert.AreNotEqual(firstPage.Items.Single().CustomerProductId, secondPage.Items.Single().CustomerProductId);
    });

    /// <summary>预取消传递给实际查询，调用方仍可回滚并释放当前事务。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductCatalogIntegration")]
    public Task Lookup_CancellationPropagates() => Rollback(async context =>
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new PostgreSqlProductRepository(context)
            .SearchSalesOrderProductsAsync(new(), cancellation.Token));
    });

    /// <summary>保持真实会话共享且只回滚，无文件操作、DDL或对既有数据的修改。</summary>
    private static async Task Rollback(Func<RepositoryBaseContext, Task> scenario)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        var user = new User();
        try { await scenario(new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user)); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>原样按公共映射绑定最小随机夹具，审计由既有能力打戳。</summary>
    private static async Task Insert(RepositoryBaseContext context, BaseEntity entity)
    {
        context.AuditStamp.StampForInsert(entity);
        var map = EntityMetadataCache.GetMetadata(entity.GetType());
        var columns = string.Join(",", map.Columns.Select(c => PostgreSqlNames.Quote(c.ColumnName)));
        var parameters = string.Join(",", map.Columns.Select(c => "@" + c.ParameterName));
        await context.Session.Connection.ExecuteAsync($"INSERT INTO public.{PostgreSqlNames.Quote(map.TableName)} ({columns}) VALUES ({parameters})",
            PostgreSqlParameters.Entity(entity, map), context.Session.Transaction);
    }

    /// <summary>提供满足现有非空约束的产品，不伪造业务单据。</summary>
    private static Product Product(string sku, bool deleted = false) => new() { Sku = sku, SupplyType = "Purchase", IsDeleted = deleted };
    /// <summary>允许未关联产品的客户独立资料，用于验证选择器不误联。</summary>
    private static CustomerProduct Relation(Guid customer, Guid? product, string code) => new()
        { CustomerId = customer, SkuId = product, CustomerCode = "fixture", CustomerSku = code, Sku = code };
    private static string Prefix() => "PGSL" + Guid.NewGuid().ToString("N")[..12];
    /// <summary>测试审计身份不模拟认证或扩大权限。</summary>
    private sealed class User : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_product_lookup__";
        public bool IsAuthenticated => false;
    }
}
