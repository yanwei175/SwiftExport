using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.MasterData;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>仅供主控在固定专用 PG 库串行验收；随机实体都在 Serializable 事务内回滚，不初始化结构或提交数据。</summary>
[TestClass]
[TestCategory("PostgreSqlProcurementMasterDataQueryIntegration")]
[DoNotParallelize]
public sealed class PostgreSqlProcurementMasterDataQueryMigrationIntegrationTests
{
    /// <summary>五投影中的双方候选与历史筛选区别停用、软删除、空白名称；客户名称允许空，供应商名称不允许。</summary>
    [TestMethod]
    public Task Parties_NewAndHistoryKeepDifferentEligibility() => InRollback(async (context, repo) =>
    {
        var prefix = Prefix();
        var customers = Enumerable.Range(0, 3).Select(index => CustomerRow(prefix + index)).ToArray();
        customers[0].CustomerName = null; customers[1].IsActive = false;
        foreach (var row in customers) await Add(context, row);
        await Delete<Customer>(context, customers[2].Uid);
        var suppliers = Enumerable.Range(0, 4).Select(index => SupplierRow(prefix + index)).ToArray();
        suppliers[1].IsActive = false; suppliers[2].SupplierName = "\t\u3000\u00A0";
        foreach (var row in suppliers) await Add(context, row);
        await Delete<Supplier>(context, suppliers[3].Uid);
        var request = new PagedRequestBase { Keyword = prefix, PageSize = 100 };
        var newCustomers = await repo.SearchCustomersAsync(request, default);
        Assert.AreEqual(customers[0].Uid, newCustomers.Items.Single().Uid); Assert.AreEqual("", newCustomers.Items.Single().CustomerName);
        var oldCustomers = await repo.SearchCustomerHistoryFiltersAsync(request, default);
        Assert.AreEqual(2, oldCustomers.TotalCount); Assert.IsFalse(oldCustomers.Items.Single(row => row.Uid == customers[1].Uid).IsActive);
        var newSuppliers = await repo.SearchSuppliersAsync(request, default);
        Assert.AreEqual(suppliers[0].Uid, newSuppliers.Items.Single().Uid); Assert.AreEqual(suppliers[0].SupplierName, newSuppliers.Items.Single().SupplierName);
        var oldSuppliers = await repo.SearchSupplierHistoryFiltersAsync(request, default);
        Assert.AreEqual(3, oldSuppliers.TotalCount); Assert.IsFalse(oldSuppliers.Items.Single(row => row.Uid == suppliers[1].Uid).IsActive);
        Assert.AreEqual(suppliers[2].SupplierName, oldSuppliers.Items.Single(row => row.Uid == suppliers[2].Uid).SupplierName);
    });

    /// <summary>随机 Unicode 空白编码/SKU 不进入新增；历史仍可按随机名称找回空白编码双方。</summary>
    [TestMethod]
    public Task UnicodeWhitespace_IsExcludedOnlyByNewEligibility() => InRollback(async (context, repo) =>
    {
        var prefix = Prefix(); var blank = RandomWhitespace();
        var customer = CustomerRow(blank); customer.CustomerName = prefix;
        var supplier = SupplierRow(blank); supplier.SupplierName = prefix;
        var product = ProductRow(blank); product.DescriptionZh = prefix;
        await Add(context, customer); await Add(context, supplier); await Add(context, product);
        var request = new PagedRequestBase { Keyword = prefix };
        Assert.AreEqual(0, (await repo.SearchCustomersAsync(request, default)).TotalCount);
        Assert.AreEqual(0, (await repo.SearchSuppliersAsync(request, default)).TotalCount);
        Assert.AreEqual(customer.Uid, (await repo.SearchCustomerHistoryFiltersAsync(request, default)).Items.Single().Uid);
        Assert.AreEqual(supplier.Uid, (await repo.SearchSupplierHistoryFiltersAsync(request, default)).Items.Single().Uid);
        Assert.AreEqual(0, (await repo.SearchProductsAsync(new() { Keyword = prefix }, default)).TotalCount);
    });

    /// <summary>空身份不进入新增或历史结果；若测试库已有空身份仅只读验证，否则只转换本例随机行并回滚。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)]
    public Task EmptyIdentity_IsNeverSelectable(int kind) => InRollback(async (context, repo) =>
    {
        var table = kind switch { 0 => "customer", 1 => "supplier", _ => "product" };
        var codeColumn = kind switch { 0 => "customer_code", 1 => "supplier_code", _ => "sku" };
        var existing = await context.Session.Connection.QuerySingleOrDefaultAsync<string>(
            $"SELECT {codeColumn} FROM public.{table} WHERE uid=@EmptyUid", new { EmptyUid = Guid.Empty }, context.Session.Transaction);
        var exists = await context.Session.Connection.ExecuteScalarAsync<bool>(
            $"SELECT EXISTS(SELECT 1 FROM public.{table} WHERE uid=@EmptyUid)", new { EmptyUid = Guid.Empty }, context.Session.Transaction);
        var prefix = Prefix();
        if (!exists)
        {
            BaseEntity row;
            if (kind == 0) { var entity = CustomerRow(prefix); await Add(context, entity); row = entity; }
            else if (kind == 1) { var entity = SupplierRow(prefix); await Add(context, entity); row = entity; }
            else { var entity = ProductRow(prefix); await Add(context, entity); row = entity; }
            Assert.AreEqual(1, await context.Session.Connection.ExecuteAsync(
                $"UPDATE public.{table} SET uid=@EmptyUid WHERE uid=@Uid", new { EmptyUid = Guid.Empty, row.Uid }, context.Session.Transaction));
        }
        var request = new ProcurementProductSearchRequest { Keyword = exists ? existing : prefix, PageSize = 100 };
        var result = await Page(repo, kind, request, default); Assert.IsFalse(result.Ids.Contains(Guid.Empty));
        if (!exists) Assert.AreEqual(0, result.Count);
        if (kind < 2)
        {
            var history = await Page(repo, kind + 3, request, default); Assert.IsFalse(history.Ids.Contains(Guid.Empty));
            if (!exists) Assert.AreEqual(0, history.Count);
        }
    });

    /// <summary>产品不依赖关系存在，未知物料与单位只显示；停用、软删及非采购产品被排除，无供应商价格始终 NULL。</summary>
    [TestMethod]
    public Task Products_RemainRootWithUnknownTypesAndUnits() => InRollback(async (context, repo) =>
    {
        var prefix = Prefix(); var rows = Enumerable.Range(0, 6).Select(index => ProductRow(prefix + index)).ToArray();
        rows[0].MaterialType = "虚拟"; rows[0].Unit = null;
        rows[1].MaterialType = "未知物料"; rows[1].Unit = "UNKNOWN";
        rows[2].Unit = "pcs"; rows[3].IsActive = false; rows[4].SupplyType = "生产";
        foreach (var row in rows) await Add(context, row);
        await Delete<Product>(context, rows[5].Uid);
        var page = await repo.SearchProductsAsync(new() { Keyword = prefix, PageSize = 100 }, default);
        Assert.AreEqual(3, page.TotalCount);
        Assert.AreEqual("", page.Items.Single(row => row.Uid == rows[0].Uid).Unit);
        Assert.AreEqual("虚拟", page.Items.Single(row => row.Uid == rows[0].Uid).MaterialType);
        Assert.AreEqual("UNKNOWN", page.Items.Single(row => row.Uid == rows[1].Uid).Unit);
        Assert.IsTrue(page.Items.All(row => row.SupplierSku == "" && row.CustomerSku == "" && row.UnitPrice is null && row.TaxPrice is null));
        var missing = await repo.SearchProductsAsync(new() { Keyword = prefix, CustomerId = Guid.NewGuid(), SupplierId = Guid.NewGuid(), PageSize = 100 }, default);
        Assert.AreEqual(3, missing.TotalCount); Assert.IsTrue(missing.Items.All(row => row.SupplierSku == "" && row.UnitPrice is null));
    });

    /// <summary>有效客户关系按 PG UUID 字节序取首项；供应商价格沿用源库 decimal(18,4)，未选供应商不代用默认关系。</summary>
    [TestMethod]
    public Task ProductRelations_KeepPriorityProjectionAndMissingBehavior() => InRollback(async (context, repo) =>
    {
        var prefix = Prefix(); var customer = CustomerRow(Prefix()); var supplier = SupplierRow(Prefix());
        await Add(context, customer); await Add(context, supplier);
        var color = new ProductColor { ColorCodeUnique = Prefix(), ColorCode = Prefix(), ColorNameZh = "颜色说明" }; await Add(context, color);
        var product = ProductRow(prefix); product.DefaultSupplierId = supplier.Uid; product.ColorId = color.Uid;
        product.MainImage = "图片"; product.LinkSku = "关联"; product.FeaturesZh = "配置"; product.DescriptionZh = "中文"; product.DescriptionEn = "English";
        await Add(context, product);
        var relations = Enumerable.Range(0, 4).Select(index => new CustomerProduct
        { CustomerId = customer.Uid, CustomerCode = customer.CustomerCode, SkuId = product.Uid, CustomerSku = Prefix() + index, IsActive = index != 2 }).ToArray();
        foreach (var relation in relations) await Add(context, relation);
        await Delete<CustomerProduct>(context, relations[3].Uid);
        var expected = relations.Take(2).OrderBy(row => row.Uid.ToString("N"), StringComparer.Ordinal).First();
        var supplierRelation = new SupplierProduct { SupplierId = supplier.Uid, SupplierCode = supplier.SupplierCode,
            SkuId = product.Uid, ProductSku = product.Sku, SupplierSku = Prefix(), UnitPrice = 1.234567m, TaxPrice = 1.395061m };
        await Add(context, supplierRelation);
        var request = new ProcurementProductSearchRequest { Keyword = prefix, CustomerId = customer.Uid, SupplierId = supplier.Uid };
        var row = (await repo.SearchProductsAsync(request, default)).Items.Single();
        Assert.AreEqual(expected.CustomerSku, row.CustomerSku); Assert.AreEqual(supplierRelation.SupplierSku, row.SupplierSku);
        // 主控已只读核实 SQL Server 和 PG 两列均为 (18,4)，六位输入应由存储舍入，不能把查询迁移变成精度变更。
        Assert.AreEqual(1.2346m, row.UnitPrice); Assert.AreEqual(1.3951m, row.TaxPrice);
        Assert.AreEqual("颜色说明", row.FinishZh); Assert.AreEqual("图片", row.MainImage); Assert.AreEqual("关联", row.LinkSku);
        Assert.AreEqual("配置", row.FeaturesZh); Assert.AreEqual("中文", row.DescriptionZh); Assert.AreEqual("English", row.DescriptionEn);
        request.Keyword = expected.CustomerSku; Assert.AreEqual(product.Uid, (await repo.SearchProductsAsync(request, default)).Items.Single().Uid);
        request.Keyword = supplierRelation.SupplierSku; Assert.AreEqual(product.Uid, (await repo.SearchProductsAsync(request, default)).Items.Single().Uid);
        request.Keyword = prefix; request.SupplierId = null;
        row = (await repo.SearchProductsAsync(request, default)).Items.Single(); Assert.AreEqual("", row.SupplierSku); Assert.IsNull(row.UnitPrice); Assert.IsNull(row.TaxPrice);
        await Delete<SupplierProduct>(context, supplierRelation.Uid); await Delete<ProductColor>(context, color.Uid);
        request.SupplierId = supplier.Uid; row = (await repo.SearchProductsAsync(request, default)).Items.Single();
        Assert.AreEqual("", row.SupplierSku); Assert.AreEqual("", row.FinishZh); Assert.IsNull(row.UnitPrice);
    });

    /// <summary>五入口对百分号、下划线、左方括号和转义符均按字面检索，并复用 ICU 大小写比较。</summary>
    [TestMethod]
    [DataRow("%")] [DataRow("_")] [DataRow("[")] [DataRow("~")]
    public Task LiteralKeywords_DoNotExpandWildcards(string special) => InRollback(async (context, repo) =>
    {
        var prefix = Prefix();
        var literal = prefix + special; var ordinary = prefix + "X";
        foreach (var code in new[] { literal, ordinary })
        { await Add(context, CustomerRow(code)); await Add(context, SupplierRow(code)); await Add(context, ProductRow(code)); }
        var request = new PagedRequestBase { Keyword = literal.ToLowerInvariant() };
        Assert.AreEqual(1, (await repo.SearchCustomersAsync(request, default)).TotalCount);
        Assert.AreEqual(1, (await repo.SearchSuppliersAsync(request, default)).TotalCount);
        Assert.AreEqual(1, (await repo.SearchCustomerHistoryFiltersAsync(request, default)).TotalCount);
        Assert.AreEqual(1, (await repo.SearchSupplierHistoryFiltersAsync(request, default)).TotalCount);
        Assert.AreEqual(1, (await repo.SearchProductsAsync(new() { Keyword = request.Keyword }, default)).TotalCount);
    });

    /// <summary>五入口跨页计数稳定、无重复和缺失；超尾页仍返回总数，预取消抛出异常但事务继续由测试拥有。</summary>
    [TestMethod]
    public Task PaginationAndCancellation_PreserveFiveQueryContracts() => InRollback(async (context, repo) =>
    {
        var prefix = Prefix();
        foreach (var index in Enumerable.Range(0, 3))
        { await Add(context, CustomerRow(prefix + index)); await Add(context, SupplierRow(prefix + index)); await Add(context, ProductRow(prefix + index)); }
        for (var kind = 0; kind < 5; kind++)
        {
            var ids = new List<Guid>();
            for (var pageIndex = 1; pageIndex <= 4; pageIndex++)
            {
                var result = await Page(repo, kind, new() { Keyword = prefix, PageIndex = pageIndex, PageSize = 1 }, default);
                Assert.AreEqual(3, result.Count); Assert.HasCount(pageIndex <= 3 ? 1 : 0, result.Ids); ids.AddRange(result.Ids);
            }
            Assert.AreEqual(3, ids.Distinct().Count());
            await Assert.ThrowsAsync<OperationCanceledException>(() => Page(repo, kind, new() { Keyword = prefix }, new CancellationToken(true)));
        }
        Assert.IsNotNull(context.Session.Transaction); Assert.AreEqual(IsolationLevel.Serializable, context.Session.Transaction.IsolationLevel);
    });

    /// <summary>统一读取五类分页元信息；所有分支都调用真实仓储，不自行查询实体代替结果。</summary>
    private static async Task<(int Count, Guid[] Ids)> Page(PostgreSqlProcurementMasterDataQueryRepository repo, int kind, ProcurementProductSearchRequest request, CancellationToken token)
    {
        switch (kind)
        {
            case 0: var c = await repo.SearchCustomersAsync(request, token); return (c.TotalCount, c.Items.Select(row => row.Uid).ToArray());
            case 1: var s = await repo.SearchSuppliersAsync(request, token); return (s.TotalCount, s.Items.Select(row => row.Uid).ToArray());
            case 2: var p = await repo.SearchProductsAsync(request, token); return (p.TotalCount, p.Items.Select(row => row.Uid).ToArray());
            case 3: var ch = await repo.SearchCustomerHistoryFiltersAsync(request, token); return (ch.TotalCount, ch.Items.Select(row => row.Uid).ToArray());
            case 4: var sh = await repo.SearchSupplierHistoryFiltersAsync(request, token); return (sh.TotalCount, sh.Items.Select(row => row.Uid).ToArray());
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    /// <summary>严格复用固定专用库校验；只创建当前随机数据并无条件回滚，不泄露连接内容。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, PostgreSqlProcurementMasterDataQueryRepository, Task> action)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        using var tx = session.Connection.BeginTransaction(IsolationLevel.Serializable); session.Transaction = tx;
        var user = new QueryUser(); var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        try { await action(context, new(context)); }
        finally { try { tx.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>新增只属于本例的随机实体，沿用现有元数据和审计，不创建辅助表。</summary>
    private static async Task Add<T>(RepositoryBaseContext context, T row) where T : BaseEntity
        => Assert.IsTrue((await new PostgreSqlDapperRepository<T>(context).AddAsync(row)).Success);

    /// <summary>只软删本例身份，不删除现有数据库数据。</summary>
    private static async Task Delete<T>(RepositoryBaseContext context, Guid uid) where T : BaseEntity
        => Assert.IsTrue((await new PostgreSqlDapperRepository<T>(context).DeleteAsync(uid, 1)).Success);

    /// <summary>生成随机编码作为独立检索边界。</summary>
    private static string Prefix() => "PGQ" + Guid.NewGuid().ToString("N");

    /// <summary>以 GUID 位构造随机纯空白编码，避免使用共享的固定空白唯一键。</summary>
    private static string RandomWhitespace() => string.Concat(Guid.NewGuid().ToString("N").Select(character => (Convert.ToInt32(character.ToString(), 16) & 1) == 0 ? '\t' : '\u3000'));

    /// <summary>客户夹具名称可空，资料关联编号始终为本例随机值。</summary>
    private static Customer CustomerRow(string code) => new() { CustomerCode = code, CustomerName = "客户", DocRelateNo = Prefix() };

    /// <summary>供应商夹具仅填充当前实体必填字段。</summary>
    private static Supplier SupplierRow(string code) => new() { SupplierCode = code, SupplierName = "供应商", DocRelateNo = Prefix() };

    /// <summary>构造无目录依赖的采购产品，单位与物料按查询投影直接保留。</summary>
    private static Product ProductRow(string code) => new() { Sku = code, SupplyType = "采购", MaterialType = "成品", Unit = "PCS" };

    /// <summary>仅用于随机测试数据的审计身份。</summary>
    private sealed class QueryUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_procurement_query__";
        public bool IsAuthenticated => false;
    }
}
