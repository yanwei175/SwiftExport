using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Infrastructure.Repositories.Products.Catalog;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用离线提供者夹具检查产品参数、投影及过滤边界，不连接数据库。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>两种提供者共享获批关系语义和外层事务，读取不加启停过滤或数据库锁。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CatalogCustomerCodes_UsesRelationshipAndCallerTransaction(bool postgresql)
    {
        using var fixture = new Fixture();
        SwiftExport.Core.Interfaces.Products.Catalog.IProductRepository repository = postgresql
            ? new PostgreSqlProductRepository(DictionaryContext(fixture))
            : new ProductRepository(DictionaryContext(fixture));
        fixture.Connection.Rows.Columns.Add("code", typeof(string));
        fixture.Connection.Rows.Rows.Add("CURRENT");
        CollectionAssert.AreEqual(new[] { "CURRENT" }, (await repository.GetAllCustomerCodeListAsync()).ToArray());
        var sql = fixture.Connection.Text.ToLowerInvariant();
        StringAssert.Contains(sql, postgresql ? "public.customer_product" : "dbo.customerproduct");
        StringAssert.Contains(sql, postgresql ? "p.uid=cp.sku_id" : "p.uid=cp.skuid");
        StringAssert.Contains(sql, postgresql ? "cp.customer_id=c.uid" : "cp.customerid=c.uid");
        StringAssert.Contains(sql, "exists (");
        StringAssert.Contains(sql, postgresql ? "c.is_deleted=false" : "c.isdeleted=0");
        StringAssert.Contains(sql, postgresql ? "p.is_deleted=false" : "p.isdeleted=0");
        StringAssert.Contains(sql, postgresql ? "cp.is_deleted=false" : "cp.isdeleted=0");
        Assert.IsFalse(sql.Contains("is_active") || sql.Contains("isactive") || sql.Contains("for update"));
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>空 BOM、空 SKU 集合和空候选无需数据库；空请求候选明确报错。</summary>
    [TestMethod]
    public async Task CatalogEmptyInputs_DoNotIssueQueries()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlProductRepository(DictionaryContext(fixture));
        Assert.IsNull(await repository.GetByBomNoAsync(" \t"));
        Assert.HasCount(0, await repository.GetBySkuListAsync(null!));
        Assert.HasCount(0, await repository.GetBySkuListAsync([" ", "\t"]));
        Assert.HasCount(0, await repository.SearchLookupAsync(new() { Keyword = " " }));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => repository.SearchLookupAsync(null!));
        Assert.HasCount(0, fixture.Connection.Commands);
    }

    /// <summary>不同入口保留输入 Trim 差异、软删范围与显式实体列别名，不自动排除停用产品。</summary>
    [TestMethod]
    public async Task CatalogCodes_KeepNormalizationAndDeletedBoundaries()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlProductRepository(DictionaryContext(fixture));
        await repository.GetBySkuAsync(" Ab ");
        Assert.AreEqual(" Ab ", fixture.Connection.Values["Sku"]);
        StringAssert.Contains(fixture.Connection.Text, "p.is_deleted=FALSE ORDER BY p.uid");
        StringAssert.Contains(fixture.Connection.Text, "p.\"product_master_id\" AS \"ProductMasterId\"");
        await repository.GetByBomNoAsync(" BOM ");
        Assert.AreEqual("BOM", fixture.Connection.Values["BomNo"]);
        await repository.GetBySkuListAsync([" Ab ", "AB", "", "CD"]);
        CollectionAssert.AreEqual(new[] { "Ab", "CD" }, (string[])fixture.Connection.Values["SkuList"]!);
        StringAssert.Contains(fixture.Connection.Text, "OR p.is_deleted IS NULL");
        await repository.GetExistingSkuListAsync([" Ab ", "AB", ""]);
        CollectionAssert.AreEqual(new[] { " Ab ", "AB", "" }, (string[])fixture.Connection.Values["SkuList"]!);
        Assert.IsFalse(fixture.Connection.Text.Contains("is_deleted", StringComparison.Ordinal));
        foreach (var command in fixture.Connection.Commands)
        {
            StringAssert.Contains(command.Sql, "rtrim(");
            StringAssert.Contains(command.Sql, "COLLATE public.erp_dictionary_ci");
            Assert.IsFalse(command.Sql.Contains("FOR UPDATE", StringComparison.Ordinal));
            Assert.IsFalse(command.Sql.Contains("is_active=", StringComparison.Ordinal));
            Assert.AreSame(fixture.Session.Transaction, command.Transaction);
        }
    }

    /// <summary>按 ID 详情保留原启停列，DTO 无对应属性；实体读取映射 false，其他 DTO 入口不扩展字段。</summary>
    [TestMethod]
    public async Task CatalogDetails_KeepDistinctProjectionAndInactiveValue()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("Sku", typeof(string));
        fixture.Connection.Rows.Columns.Add("IsActive", typeof(bool));
        fixture.Connection.Rows.Columns.Add("ManufacturerId", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("ManufacturerName", typeof(string));
        var manufacturer = Guid.NewGuid();
        fixture.Connection.Rows.Rows.Add("P01", false, manufacturer, DBNull.Value);
        var repository = new PostgreSqlProductRepository(DictionaryContext(fixture));
        var detail = await repository.GetDtoByIdAsync(Guid.NewGuid());
        Assert.AreEqual(manufacturer, detail!.ManufacturerId);
        Assert.IsNull(detail.ManufacturerName);
        StringAssert.Contains(fixture.Connection.Text, "p.is_active AS \"IsActive\"");
        Assert.IsFalse(fixture.Connection.Text.Contains("product_master pm", StringComparison.Ordinal));
        Assert.IsFalse((await repository.GetBySkuAsync("P01"))!.IsActive);
        await repository.GetDtoBySkuAsync("P01");
        Assert.IsFalse(fixture.Connection.Text.Contains("AS \"IsActive\"", StringComparison.Ordinal));
        StringAssert.Contains(fixture.Connection.Text, "COALESCE(pm.category_id,@EmptyGuid) AS \"CategoryId\"");
        StringAssert.Contains(fixture.Connection.Text, "manufacturer.is_deleted=FALSE");
        StringAssert.Contains(fixture.Connection.Text, "defaultSupplier.is_deleted IS NULL");
    }

    /// <summary>候选保留裸百分号下划线模式、原优先级及上限，明确不把客户关系 JOIN 到分页。</summary>
    [TestMethod]
    [DataRow(0, 20)]
    [DataRow(999, 50)]
    [DataRow(1, 1)]
    public async Task CatalogLookup_KeepsRawPatternsAndPriority(int requested, int expected)
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlProductRepository(DictionaryContext(fixture));
        await repository.SearchLookupAsync(new() { Keyword = " A_% ", Limit = requested });
        Assert.AreEqual("A_%", fixture.Connection.Values["ExactKeyword"]);
        Assert.AreEqual("A_%%", fixture.Connection.Values["PrefixKeyword"]);
        Assert.AreEqual("%A_%%", fixture.Connection.Values["ContainsKeyword"]);
        Assert.AreEqual(expected, fixture.Connection.Values["Limit"]);
        StringAssert.Contains(fixture.Connection.Text, "THEN 0");
        StringAssert.Contains(fixture.Connection.Text, "THEN 1");
        StringAssert.Contains(fixture.Connection.Text, "THEN 2");
        StringAssert.Contains(fixture.Connection.Text, "THEN 3");
        StringAssert.Contains(fixture.Connection.Text, "ASC NULLS FIRST,p.uid LIMIT @Limit");
        Assert.IsFalse(fixture.Connection.Text.Contains("JOIN public.customer_product", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Connection.Text.Contains("ESCAPE '~'", StringComparison.Ordinal));
        StringAssert.Contains(fixture.Connection.Text, "LIKE @ContainsKeyword ESCAPE ''");
        StringAssert.Contains(fixture.Connection.Text, "LIKE @PrefixKeyword ESCAPE ''");
    }

    /// <summary>分页计数与取页共用所有筛选；分类单多选和客户单多选均独立 AND，包装不限类型且稳定取一条。</summary>
    [TestMethod]
    public async Task CatalogPage_PreservesFiltersCapsAndPackaging()
    {
        using var fixture = new Fixture();
        var category = Guid.NewGuid();
        var color = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var repository = new PostgreSqlProductRepository(DictionaryContext(fixture));
        var page = await repository.GetPagedAsync(new()
        {
            Keyword = " A?%_[~ B ", CategoryName = " Cat_% ", ColorCodeUnique = " Color ",
            CategoryId = category, CategoryIds = [Guid.Empty, category, category], ColorIds = [color, color],
            CustomerId = customer, CustomerIds = [customer, Guid.Empty], CustomerCodes = [" C01 ", "c01"],
            MaterialTypeList = [" Metal ", "metal"], BatchKeywords = Enumerable.Range(0, 301).Select(index => "K" + index).ToList(),
            PageIndex = 0, PageSize = 999
        });
        Assert.AreEqual(1, page.PageIndex);
        Assert.AreEqual(500, page.PageSize);
        Assert.HasCount(2, fixture.Connection.Commands);
        foreach (var command in fixture.Connection.Commands)
        {
            Assert.AreEqual("%%A_~%~_~[~~%B%%", command.Values["Keyword"]);
            Assert.AreEqual("%Cat_%%", command.Values["CategoryName"]);
            Assert.AreEqual("Color", command.Values["ColorCodeUnique"]);
            Assert.HasCount(300, (string[])command.Values["BatchKeywords"]!);
            CollectionAssert.AreEqual(new[] { "Metal" }, (string[])command.Values["MaterialTypeList"]!);
            CollectionAssert.AreEqual(new[] { "C01" }, (string[])command.Values["CustomerCodes"]!);
            CollectionAssert.AreEqual(new[] { category }, (Guid[])command.Values["CategoryIds"]!);
            CollectionAssert.AreEqual(new[] { color }, (Guid[])command.Values["ColorIds"]!);
            CollectionAssert.AreEqual(new[] { customer }, (Guid[])command.Values["CustomerIds"]!);
            StringAssert.Contains(command.Sql, "pm.category_id=@CategoryId AND pm.category_id=ANY(@CategoryIds)");
            StringAssert.Contains(command.Sql, "cp.customer_id=@CustomerId");
            StringAssert.Contains(command.Sql, "cp.customer_id=ANY(@CustomerIds)");
            StringAssert.Contains(command.Sql, "c.customer_code");
            Assert.IsFalse(command.Sql.Contains("FOR UPDATE", StringComparison.Ordinal));
            Assert.IsFalse(command.Sql.Contains("AS \"IsActive\"", StringComparison.Ordinal));
            Assert.AreSame(fixture.Session.Transaction, command.Transaction);
        }
        StringAssert.Contains(fixture.Connection.Text, "LEFT JOIN LATERAL");
        StringAssert.Contains(fixture.Connection.Text, "pat.fake_name AS \"FakeName\"");
        StringAssert.Contains(fixture.Connection.Text, "pc.color_code_unique AS \"ColorCodeUnique\"");
        StringAssert.Contains(fixture.Connection.Text, "pc.color_code AS \"ColorCode\"");
        StringAssert.Contains(fixture.Connection.Text, "pc.color_name_en AS \"ColorNameEn\"");
        Assert.AreEqual(NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid, fixture.Connection.Types["CategoryIds"]);
        Assert.AreEqual(NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid, fixture.Connection.Types["ColorIds"]);
        Assert.AreEqual(NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid, fixture.Connection.Types["CustomerIds"]);
        StringAssert.Contains(fixture.Connection.Text, "pp.is_default DESC NULLS LAST,pp.pcs_per_package DESC NULLS LAST,pp.uid LIMIT 1");
        Assert.IsFalse(fixture.Connection.Text.Contains("package_type=", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Connection.Text.Contains("JOIN public.customer_product", StringComparison.Ordinal));
        await repository.GetPagedAsync(null!);
        Assert.AreEqual(20, fixture.Connection.Values["Limit"]);
    }
}
