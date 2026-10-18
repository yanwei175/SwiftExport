using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Infrastructure.Repositories.Products.Catalog;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用离线提供者夹具检查产品参数、投影及过滤边界，不连接数据库。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>PG 保留获批客户关系、返回值和外层事务，读取不加启停过滤或数据库锁。</summary>
    [TestMethod]
    public async Task CatalogCustomerCodes_UsesRelationshipAndCallerTransaction()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlProductRepository(DictionaryContext(fixture));
        fixture.Connection.Rows.Columns.Add("code", typeof(string));
        fixture.Connection.Rows.Rows.Add("CURRENT");
        CollectionAssert.AreEqual(new[] { "CURRENT" }, (await repository.GetAllCustomerCodeListAsync()).ToArray());
        var sql = fixture.Connection.Text.ToLowerInvariant();
        StringAssert.Contains(sql, "public.customer_product");
        StringAssert.Contains(sql, "p.uid=cp.sku_id");
        StringAssert.Contains(sql, "cp.customer_id=c.uid");
        StringAssert.Contains(sql, "exists (");
        StringAssert.Contains(sql, "c.is_deleted=false");
        StringAssert.Contains(sql, "p.is_deleted=false");
        StringAssert.Contains(sql, "cp.is_deleted=false");
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

    /// <summary>分页计数与取页共用所有筛选；分类和客户筛选独立 AND，包装只取有效外箱，不回退其他类型。</summary>
    [TestMethod]
    public async Task CatalogPage_PreservesFiltersCapsAndPackaging()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("total_count", typeof(int));
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
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
        Assert.HasCount(1, fixture.Connection.Commands);
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
        StringAssert.Contains(fixture.Connection.Text, "LEFT JOIN public.product_package pkg ON pkg.owner_product_id=matched.uid");
        StringAssert.Contains(fixture.Connection.Text, "pat.fake_name AS \"FakeName\"");
        StringAssert.Contains(fixture.Connection.Text, "ELSE COALESCE(pat.fake_name,'') END AS \"CategoryName\"");
        StringAssert.Contains(fixture.Connection.Text, "WHEN pat.uid IS NULL THEN '分类已失效'");
        StringAssert.Contains(fixture.Connection.Text, "pc.color_code_unique AS \"ColorCodeUnique\"");
        StringAssert.Contains(fixture.Connection.Text, "pc.color_code AS \"ColorCode\"");
        StringAssert.Contains(fixture.Connection.Text, "pc.color_name_en AS \"ColorNameEn\"");
        Assert.AreEqual(NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid, fixture.Connection.Types["CategoryIds"]);
        Assert.AreEqual(NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid, fixture.Connection.Types["ColorIds"]);
        Assert.AreEqual(NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid, fixture.Connection.Types["CustomerIds"]);
        StringAssert.Contains(fixture.Connection.Text, "pkg.package_type=@CartonPackageType AND pkg.is_deleted=FALSE");
        Assert.AreEqual(SwiftExport.Contracts.SysEnums.ProductPackageTypeCodes.Carton, fixture.Connection.Values["CartonPackageType"]);
        Assert.IsFalse(fixture.Connection.Text.Contains("is_default", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Connection.Text.Contains("JOIN public.customer_product", StringComparison.Ordinal));
        await repository.GetPagedAsync(null!);
        Assert.AreEqual(20, fixture.Connection.Values["Limit"]);
    }
}
