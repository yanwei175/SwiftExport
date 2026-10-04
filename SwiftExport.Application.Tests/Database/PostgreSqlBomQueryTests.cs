using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Infrastructure.Repositories.Products;

namespace SwiftExport.Application.Tests.Database;

/// <summary>BOM 普通读取离线测试，复用记录连接检查真实 Dapper 投影、参数与共享事务，不触及数据库。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>按指定版本读取的无效编号或版本直接返回，不能发出 SQL 或冒称当前结构读取。</summary>
    [TestMethod]
    [DataRow(null, 1)]
    [DataRow(" ", 1)]
    [DataRow("BOM", 0)]
    [DataRow("BOM", -1)]
    public async Task BomQuery_InvalidVersionInputDoesNotIssueCommands(string? code, int version)
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlBomRepository(DictionaryContext(fixture));
        Assert.HasCount(0, await repository.GetByBomNoAndVersionAsync(code!, version));
        Assert.HasCount(0, fixture.Connection.Commands);
    }

    /// <summary>DTO 当前读取显式映射组件快照与中文展示，缺失子产品展示保持 NULL，不丢组成行。</summary>
    [TestMethod]
    public async Task BomQuery_DtoMapsSnapshotAndNullableDisplay()
    {
        using var fixture = new Fixture();
        var uid = Guid.NewGuid();
        var component = Guid.NewGuid();
        foreach (var field in new[] { "BomNo", "SubSku", "Description", "Finish", "Features", "MainImage", "Note" })
            fixture.Connection.Rows.Columns.Add(field, typeof(string));
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("SubSkuId", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("Numerator", typeof(int));
        fixture.Connection.Rows.Columns.Add("Denominator", typeof(int));
        fixture.Connection.Rows.Rows.Add("BOM-A", "old-component", DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, "note", uid, component, 2, 3);
        var repository = new PostgreSqlBomRepository(DictionaryContext(fixture));
        var dto = (await repository.GetBomDtoByBomNoAsync(" BOM-A ")).Single();
        Assert.AreEqual(uid, dto.Uid);
        Assert.AreEqual(component, dto.SubSkuId);
        Assert.AreEqual("old-component", dto.SubSku);
        Assert.AreEqual("note", dto.Note);
        Assert.AreEqual(2, dto.Numerator);
        Assert.AreEqual(3, dto.Denominator);
        Assert.IsNull(dto.Description);
        Assert.IsNull(dto.Finish);
        Assert.IsNull(dto.Features);
        Assert.IsNull(dto.MainImage);
        Assert.AreEqual("BOM-A", fixture.Connection.Values["BomNo"]);
        var sql = BomQuerySql(fixture.Connection.Text);
        StringAssert.Contains(sql, "innerjoinpublic.productparent");
        StringAssert.Contains(sql, "leftjoinpublic.productchild");
        StringAssert.Contains(sql, "leftjoinpublic.product_colorcolor");
        StringAssert.Contains(sql, "component_sku_snapshotassubsku");
        StringAssert.Contains(sql, "description_zhasdescription");
        StringAssert.Contains(sql, "features_zhasfeatures");
        Assert.IsFalse(sql.Contains("is_active", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("forupdate", StringComparison.Ordinal));
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>当前实体读取与指定历史版本读取都使用完整实体别名，但历史入口不 JOIN 或强求当前父。</summary>
    [TestMethod]
    public async Task BomQuery_EntityVersionReadDoesNotRequireCurrentParent()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("BomVersion", typeof(int));
        fixture.Connection.Rows.Columns.Add("ParentProductId", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("ComponentSkuSnapshot", typeof(string));
        fixture.Connection.Rows.Rows.Add(4, Guid.NewGuid(), "snapshot");
        var repository = new PostgreSqlBomRepository(DictionaryContext(fixture));
        var current = (await repository.GetBomByBomNoAsync(" BOM-A ")).Single();
        Assert.AreEqual(4, current.BomVersion);
        StringAssert.Contains(BomQuerySql(fixture.Connection.Text), "innerjoinpublic.productparent");
        Assert.AreEqual("BOM-A", fixture.Connection.Values["BomNo"]);
        var history = (await repository.GetByBomNoAndVersionAsync(" BOM-A ", 4)).Single();
        Assert.AreEqual("snapshot", history.ComponentSkuSnapshot);
        Assert.AreEqual(4, fixture.Connection.Values["BomVersion"]);
        var sql = BomQuerySql(fixture.Connection.Text);
        Assert.IsFalse(sql.Contains("join", StringComparison.Ordinal));
        StringAssert.Contains(sql, "bom_version");
        StringAssert.Contains(sql, "is_deleted=false");
        StringAssert.Contains(sql, "parent_sku_snapshotasparentskusnapshot");
        StringAssert.Contains(sql, "component_product_idascomponentproductid");
        StringAssert.Contains(sql, "collatepublic.erp_dictionary_ci");
        StringAssert.Contains(sql, "rtrim(");
        foreach (var command in fixture.Connection.Commands) Assert.AreSame(fixture.Session.Transaction, command.Transaction);
    }

    /// <summary>两个存在查询具有不同软删边界；活动查询按身份 OR 编号，旧逐项存在查询保留原始输入并包含历史软删。</summary>
    [TestMethod]
    public async Task BomQuery_ExistsKeepsDistinctScopesAndNormalization()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlBomRepository(DictionaryContext(fixture));
        var product = Guid.NewGuid();
        await repository.HasActiveBomForProductAsync(product, " BOM-A ");
        Assert.AreEqual(product, fixture.Connection.Values["ProductId"]);
        Assert.AreEqual(NpgsqlTypes.NpgsqlDbType.Uuid, fixture.Connection.Types["ProductId"]);
        Assert.AreEqual("BOM-A", fixture.Connection.Values["BomNo"]);
        var active = BomQuerySql(fixture.Connection.Text);
        StringAssert.Contains(active, "is_deleted=false");
        StringAssert.Contains(active, "or");
        Assert.IsFalse(active.Contains("join", StringComparison.Ordinal));
        await repository.HasActiveBomForProductAsync(Guid.Empty, " ");
        Assert.AreEqual(DBNull.Value, fixture.Connection.Values["BomNo"]);
        await repository.IsBomItemExistAsync(" BOM-A ", " Child ");
        Assert.AreEqual(" BOM-A ", fixture.Connection.Values["BomNo"]);
        Assert.AreEqual(" Child ", fixture.Connection.Values["SubSku"]);
        var exists = BomQuerySql(fixture.Connection.Text);
        Assert.IsFalse(exists.Contains("is_deleted", StringComparison.Ordinal));
        Assert.IsFalse(exists.Contains("bom_version", StringComparison.Ordinal));
        Assert.IsFalse(exists.Contains("join", StringComparison.Ordinal));
        StringAssert.Contains(exists, "component_sku_snapshot");
        StringAssert.Contains(exists, "rtrim(");
        foreach (var command in fixture.Connection.Commands) Assert.AreSame(fixture.Session.Transaction, command.Transaction);
    }

    /// <summary>分页保留 Build 模式、十字段搜索、三快照批量词、三百上限与显式文本数组，当前行稳定分页。</summary>
    [TestMethod]
    [DataRow(0, 20)]
    [DataRow(999, 500)]
    [DataRow(1, 1)]
    public async Task BomQuery_PageBindsOriginalSearchAndLimits(int size, int expected)
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlBomRepository(DictionaryContext(fixture));
        var count = new System.Data.DataTable();
        count.Columns.Add("TotalCount", typeof(int));
        count.Rows.Add(3);
        var items = new System.Data.DataTable();
        items.Columns.Add("Uid", typeof(Guid));
        items.Columns.Add("SubSkuId", typeof(Guid));
        items.Columns.Add("SubSku", typeof(string));
        var uid = Guid.NewGuid();
        items.Rows.Add(uid, DBNull.Value, "missing-id-snapshot");
        fixture.Connection.ResultSets = [count, items];
        var words = new List<string> { " K0 ", "k0", "", " " };
        words.AddRange(Enumerable.Range(1, 300).Select(index => "K" + index));
        var page = await repository.GetPagedAsync(new() { Keyword = " A?%_[~ B ", BatchKeywords = words, PageIndex = 0, PageSize = size });
        Assert.AreEqual(1, page.PageIndex);
        Assert.AreEqual(expected, page.PageSize);
        Assert.AreEqual(3, page.TotalCount);
        Assert.AreEqual(uid, page.Items.Single().Uid);
        Assert.AreEqual(Guid.Empty, page.Items.Single().SubSkuId);
        Assert.AreEqual("missing-id-snapshot", page.Items.Single().SubSku);
        Assert.HasCount(1, fixture.Connection.Commands);
        foreach (var command in fixture.Connection.Commands)
        {
            Assert.AreEqual("%%A_~%~_~[~~%B%%", command.Values["Keyword"]);
            Assert.AreEqual(expected, command.Values["PageSize"]);
            var values = (string[])command.Values["BatchKeywords"]!;
            Assert.HasCount(300, values);
            Assert.AreEqual("K0", values[0]);
            Assert.AreEqual("K299", values[^1]);
            var sql = BomQuerySql(command.Sql);
            foreach (var field in new[] { "bom_no", "parent_sku_snapshot", "component_sku_snapshot", "description_en", "description_zh", "features_en", "features_zh", "color_code", "color_name_en", "color_name_zh" })
                StringAssert.Contains(sql, field + "collatepublic.erp_dictionary_cilike@keywordescape'~'");
            Assert.IsFalse(sql.Contains("forupdate", StringComparison.Ordinal));
            Assert.AreSame(fixture.Session.Transaction, command.Transaction);
        }
        Assert.AreEqual(NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text, fixture.Connection.Types["BatchKeywords"]);
        var dataSql = BomQuerySql(fixture.Connection.Text);
        StringAssert.Contains(dataSql, "orderbyrtrim(b.bom_no,'')collatepublic.erp_dictionary_ci");
        StringAssert.Contains(dataSql, "b.line_number,b.uid");
        await repository.GetPagedAsync(null!);
        Assert.AreEqual(20, fixture.Connection.Values["PageSize"]);
        Assert.IsFalse(fixture.Connection.Values.ContainsKey("BatchKeywords"));
    }

    /// <summary>仅规范测试中的 SQL 空白与引用标识符，断言实际执行命令而非读取生产源码。</summary>
    private static string BomQuerySql(string sql) => System.Text.RegularExpressions.Regex.Replace(sql.Replace("\"", "", StringComparison.Ordinal), "\\s+", "").ToLowerInvariant();
}
