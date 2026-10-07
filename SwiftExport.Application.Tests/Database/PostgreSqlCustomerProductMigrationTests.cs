using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Infrastructure.Repositories.Customers.CustomerProducts;

namespace SwiftExport.Application.Tests.Database;

/// <summary>客户产品迁移离线验证复用原记录连接，检查原始入参、完整投影和共享事务，不访问网络。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>空集合不执行命令；原需枚举的 items 和客户集合空值仍明确失败，不静默吞调用错误。</summary>
    [TestMethod]
    public async Task CustomerProductEmptyCollections_KeepBoundary()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlCustomerProductRepository(DictionaryContext(fixture));
        Assert.HasCount(0, await repository.GetExistingCustomerSkuListAsync([], Guid.Empty));
        Assert.HasCount(0, await repository.GetExistingCustomerSkuListAsync(null!, Guid.Empty));
        Assert.HasCount(0, await repository.GetCustomerSkusUsedByOtherRecordsAsync(Guid.Empty, []));
        Assert.HasCount(0, await repository.GetCustomerIdsWithProductsAsync([]));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => repository.GetCustomerSkusUsedByOtherRecordsAsync(Guid.Empty, null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => repository.GetCustomerIdsWithProductsAsync(null!));
        Assert.HasCount(0, fixture.Connection.Commands);
    }

    /// <summary>详情映射当前客户码、关系快照和空价格，非标准产品身份为空仍允许展示且保留停用 false。</summary>
    [TestMethod]
    public async Task CustomerProductDetail_MapsNullableRelationAndCurrentDisplay()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("CustomerCode", typeof(string));
        fixture.Connection.Rows.Columns.Add("SkuId", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("Sku", typeof(string));
        fixture.Connection.Rows.Columns.Add("Description", typeof(string));
        fixture.Connection.Rows.Columns.Add("USDPrice", typeof(decimal));
        fixture.Connection.Rows.Columns.Add("IsActive", typeof(bool));
        fixture.Connection.Rows.Rows.Add("CURRENT", DBNull.Value, "SNAPSHOT", DBNull.Value, DBNull.Value, false);
        var repository = new PostgreSqlCustomerProductRepository(DictionaryContext(fixture));
        var dto = await repository.GetDtoByIdAsync(Guid.NewGuid());
        Assert.AreEqual("CURRENT", dto!.CustomerCode);
        Assert.AreEqual("SNAPSHOT", dto.Sku);
        Assert.IsNull(dto.SkuId);
        Assert.IsNull(dto.Description);
        Assert.IsNull(dto.USDPrice);
        Assert.IsFalse(dto.IsActive);
        StringAssert.Contains(fixture.Connection.Text, "c.customer_code AS \"CustomerCode\"");
        StringAssert.Contains(fixture.Connection.Text, "cp.sku AS \"Sku\"");
        StringAssert.Contains(fixture.Connection.Text, "COALESCE(p.description_en,cp.description_en)");
        StringAssert.Contains(fixture.Connection.Text, "LEFT JOIN public.product p");
        StringAssert.Contains(fixture.Connection.Text, "OR c.is_deleted IS NULL");
        StringAssert.Contains(fixture.Connection.Text, "cp.customer_pd_features AS \"CustomerPdFeatures\"");
        Assert.IsFalse(fixture.Connection.Text.Contains("is_active=", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Connection.Text.Contains("FOR UPDATE", StringComparison.Ordinal));
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>分页二十条默认与两百条上限，Keyword 不 Trim，只有原客户单选及五个关键词列参与筛选。</summary>
    [TestMethod]
    [DataRow(0, 20)]
    [DataRow(999, 200)]
    [DataRow(1, 1)]
    public async Task CustomerProductPage_UsesOriginalFiltersAndStableOrder(int requestedSize, int expectedSize)
    {
        using var fixture = new Fixture();
        var customer = Guid.NewGuid();
        var repository = new PostgreSqlCustomerProductRepository(DictionaryContext(fixture));
        var page = await repository.GetPagedAsync(new()
        {
            CustomerId = customer, Keyword = " A?%_[~ B ", PageIndex = 0, PageSize = requestedSize,
            CustomerIds = [Guid.NewGuid()], CustomerCodes = ["IGNORED"], BatchKeywords = ["IGNORED"],
            CategoryId = Guid.NewGuid(), CategoryIds = [Guid.NewGuid()], CategoryName = "IGNORED", ColorCodeUnique = "IGNORED",
            MaterialType = "IGNORED", UserTag = "IGNORED"
        });
        Assert.AreEqual(1, page.PageIndex);
        Assert.AreEqual(expectedSize, page.PageSize);
        Assert.HasCount(2, fixture.Connection.Commands);
        foreach (var command in fixture.Connection.Commands)
        {
            Assert.AreEqual(customer, command.Values["CustomerId"]);
            Assert.AreEqual("%%A_~%~_~[~~%B%%", command.Values["Keyword"]);
            Assert.AreEqual(expectedSize, command.Values["PageSize"]);
            foreach (var column in new[] { "customer_sku", "sku", "link_sku", "customer_pd_description", "customer_pd_series" })
                StringAssert.Contains(command.Sql, $"cp.{column} COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'");
            Assert.IsFalse(command.Values.ContainsKey("CategoryId"));
            Assert.IsFalse(command.Sql.Contains("IGNORED", StringComparison.Ordinal));
            Assert.IsFalse(command.Sql.Contains("FOR UPDATE", StringComparison.Ordinal));
            Assert.AreSame(fixture.Session.Transaction, command.Transaction);
        }
        StringAssert.Contains(fixture.Connection.Text, "ORDER BY cp.customer_sku COLLATE public.erp_dictionary_ci ASC NULLS FIRST,cp.uid");
        await repository.GetPagedAsync(null!);
        Assert.AreEqual(20, fixture.Connection.Values["PageSize"]);
    }

    /// <summary>实体按原码查找不 Trim；集合保留空串及重复输入，普通现存查询不擅自 DISTINCT。</summary>
    [TestMethod]
    public async Task CustomerProductCodes_PreserveRawValuesAndMetadata()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("CustomerSku", typeof(string));
        fixture.Connection.Rows.Rows.Add(" Ab ");
        var repository = new PostgreSqlCustomerProductRepository(DictionaryContext(fixture));
        var customer = Guid.NewGuid();
        var entity = await repository.GetByCustomerSkuAsync(" Ab ", customer);
        Assert.AreEqual(" Ab ", entity!.CustomerSku);
        Assert.AreEqual(" Ab ", fixture.Connection.Values["CustomerSku"]);
        StringAssert.Contains(fixture.Connection.Text, "cp.\"customer_code\" AS \"CustomerCode\"");
        StringAssert.Contains(fixture.Connection.Text, "cp.\"sku_id\" AS \"SkuId\"");
        await repository.GetExistingCustomerSkuListAsync([" Ab ", "ab", "", " Ab "], customer);
        CollectionAssert.AreEqual(new[] { " Ab ", "ab", "", " Ab " }, (string[])fixture.Connection.Values["SkuList"]!);
        Assert.AreEqual(NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text, fixture.Connection.Types["SkuList"]);
        Assert.IsFalse(fixture.Connection.Text.Contains("DISTINCT", StringComparison.Ordinal));
        StringAssert.Contains(fixture.Connection.Text, "rtrim(cp.customer_sku,' ') COLLATE public.erp_dictionary_ci");
        Assert.AreEqual(customer, fixture.Connection.Values["CustomerId"]);
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>占用查询排除整个提交 UID 集合，料号按原等价分组返回原文，不错误按 tuple 配对。</summary>
    [TestMethod]
    public async Task CustomerProductOccupiedCodes_ExcludeWholeSetAndKeepRawRepresentative()
    {
        using var fixture = new Fixture();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var repository = new PostgreSqlCustomerProductRepository(DictionaryContext(fixture));
        await repository.GetCustomerSkusUsedByOtherRecordsAsync(Guid.NewGuid(), [(first, " A "), (second, "a"), (first, "")]);
        CollectionAssert.AreEqual(new[] { first, second, first }, (Guid[])fixture.Connection.Values["Uids"]!);
        CollectionAssert.AreEqual(new[] { " A ", "a", "" }, (string[])fixture.Connection.Values["Codes"]!);
        Assert.AreEqual(NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid, fixture.Connection.Types["Uids"]);
        StringAssert.Contains(fixture.Connection.Text, "NOT (cp.uid=ANY(@Uids))");
        StringAssert.Contains(fixture.Connection.Text, "DISTINCT ON (rtrim(cp.customer_sku,' ') COLLATE public.erp_dictionary_ci) cp.customer_sku AS \"CustomerSku\"");
        Assert.IsFalse(fixture.Connection.Text.Contains("JOIN public.customer", StringComparison.Ordinal));
    }

    /// <summary>关联客户集合仅去重输入，不过滤空身份或检查客户/产品活动资格。</summary>
    [TestMethod]
    public async Task CustomerProductCustomerIds_OnlyInspectRelationRows()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("CustomerId", typeof(Guid));
        var customer = Guid.NewGuid();
        fixture.Connection.Rows.Rows.Add(customer);
        var repository = new PostgreSqlCustomerProductRepository(DictionaryContext(fixture));
        Assert.AreEqual(customer, (await repository.GetAllCustomerIdListAsync()).Single());
        await repository.GetCustomerIdsWithProductsAsync([Guid.Empty, customer, customer]);
        CollectionAssert.AreEqual(new[] { Guid.Empty, customer }, (Guid[])fixture.Connection.Values["Uids"]!);
        Assert.IsFalse(fixture.Connection.Text.Contains("JOIN", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Connection.Text.Contains("is_active", StringComparison.Ordinal));
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }
}
