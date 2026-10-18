using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Infrastructure.Repositories.Products.Catalog;

namespace SwiftExport.Application.Tests.Database;

/// <summary>物料分页的空页映射及只读内联数据语义回归，不创建表、不写入业务库。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>总数与 DTO 分段映射保留越界总数，空身份占位不得进入 Items。</summary>
    [TestMethod]
    public async Task ProductPage_MapsTotalsEmptyPageAndNullableCarton()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("total_count", typeof(int));
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("PcsOfCarton", typeof(decimal));
        fixture.Connection.Rows.Columns.Add("GwOfCarton", typeof(decimal));
        fixture.Connection.Rows.Rows.Add(12, DBNull.Value, DBNull.Value, DBNull.Value);
        var repository = new PostgreSqlProductRepository(DictionaryContext(fixture));
        var page = await repository.GetPagedAsync(new() { PageIndex = 99 });
        Assert.AreEqual(12, page.TotalCount); Assert.HasCount(0, page.Items);
        fixture.Connection.Rows.Rows.Clear();
        var uid = Guid.NewGuid();
        fixture.Connection.Rows.Rows.Add(1, uid, DBNull.Value, 0m);
        page = await repository.GetPagedAsync(new() { Keyword = "needle" });
        Assert.AreEqual(1, page.TotalCount); Assert.AreEqual(uid, page.Items.Single().Uid);
        Assert.IsNull(page.Items.Single().PcsOfCarton); Assert.AreEqual(0m, page.Items.Single().GwOfCarton);
        fixture.Connection.Rows.Rows.Clear(); fixture.Connection.Rows.Rows.Add(0, DBNull.Value, DBNull.Value, DBNull.Value);
        page = await repository.GetPagedAsync(new());
        Assert.AreEqual(0, page.TotalCount); Assert.HasCount(0, page.Items);
    }

    /// <summary>真实 PG 只读 CTE 夹具覆盖九字段、软删除、NULL、Unicode、转义与稳定分页，整页投影与旧 SQL 等价。</summary>
    [TestMethod]
    public async Task ProductPage_ReadOnlyInlineSemantics()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_PRODUCT_PAGE_READONLY") != "1")
            Assert.Inconclusive("未启用本轮只读语义验证。");
        await using var connection = ProductPageConnection(); await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await connection.ExecuteAsync("SET TRANSACTION READ ONLY", transaction: transaction);
        var products = new List<Dictionary<string, object?>>();
        var colors = new List<Dictionary<string, object?>>();
        var masters = new List<Dictionary<string, object?>>();
        var categories = new List<Dictionary<string, object?>>();
        const string text = "Needle~%_[\\ X1Z Straße É e\u0301 İstanbul 中文\t尾";
        var fields = new[] { "sku", "description_en", "description_zh", "features_en", "features_zh", "color_code", "color_name_en", "color_name_zh", "category_name" };
        for (var index = 0; index < fields.Length; index++)
        {
            var row = new Dictionary<string, object?> { ["uid"] = Guid.NewGuid(), ["sku"] = "S" + index, ["is_deleted"] = false };
            if (index < 5) row[fields[index]] = text;
            else if (index < 8)
            {
                var color = Guid.NewGuid(); row["color_id"] = color;
                colors.Add(new() { ["uid"] = color, [fields[index]] = text, ["is_deleted"] = false });
            }
            else
            {
                var master = Guid.NewGuid(); var category = Guid.NewGuid(); row["product_master_id"] = master;
                masters.Add(new() { ["uid"] = master, ["category_id"] = category, ["is_deleted"] = false });
                categories.Add(new() { ["uid"] = category, ["category_name"] = text, ["fake_name"] = "显示名", ["is_deleted"] = false });
            }
            products.Add(row);
        }
        products.Add(new() { ["uid"] = Guid.NewGuid(), ["sku"] = null, ["description_en"] = text, ["is_deleted"] = null });
        products.Add(new() { ["uid"] = Guid.NewGuid(), ["sku"] = "a", ["description_en"] = "é", ["is_deleted"] = false });
        products.Add(new() { ["uid"] = Guid.NewGuid(), ["sku"] = "A", ["description_en"] = "e\u0301", ["is_deleted"] = false });
        products.Add(new() { ["uid"] = Guid.NewGuid(), ["sku"] = "deleted", ["description_en"] = text, ["is_deleted"] = true });
        var deletedColor = Guid.NewGuid();
        colors.Add(new() { ["uid"] = deletedColor, ["color_code"] = text, ["is_deleted"] = true });
        products.Add(new() { ["uid"] = Guid.NewGuid(), ["sku"] = "deleted-reference", ["color_id"] = deletedColor, ["is_deleted"] = false });
        products.Add(new() { ["uid"] = Guid.NewGuid(), ["sku"] = "dangling", ["color_id"] = Guid.NewGuid(), ["product_master_id"] = Guid.NewGuid(), ["is_deleted"] = false });
        // 有效定义包含 NULL 软删除；已删主型号或分类不能借小字典预匹配重新进入结果。
        colors[2]["is_deleted"] = null; masters[0]["is_deleted"] = null; categories[0]["is_deleted"] = null;
        foreach (var deletedMaster in new[] { false, true })
        {
            var master = Guid.NewGuid(); var category = Guid.NewGuid();
            masters.Add(new() { ["uid"] = master, ["category_id"] = category, ["is_deleted"] = deletedMaster });
            categories.Add(new() { ["uid"] = category, ["category_name"] = text, ["is_deleted"] = !deletedMaster });
            products.Add(new() { ["uid"] = Guid.NewGuid(), ["sku"] = "deleted-category-reference" + deletedMaster,
                ["product_master_id"] = master, ["is_deleted"] = false });
        }
        var tables = new Dictionary<string, object>
        {
            ["product"] = products, ["product_color"] = colors, ["product_master"] = masters, ["product_category"] = categories,
            ["supplier"] = Array.Empty<object>(), ["warehouse_site"] = Array.Empty<object>(),
            ["customer"] = Array.Empty<object>(), ["customer_product"] = Array.Empty<object>(),
            ["product_package"] = new[] { new { uid = Guid.NewGuid(), owner_product_id = (Guid)products[1]["uid"]!,
                package_type = SwiftExport.Contracts.SysEnums.ProductPackageTypeCodes.Carton, is_deleted = false,
                pcs_per_package = (decimal?)null, nw_kg = 0m, gw_kg = (decimal?)null, cbm = 0.125m } }
        };
        var fixtures = string.Join(",\n", tables.Select(pair => $"{pair.Key} AS (SELECT * FROM jsonb_populate_recordset(NULL::public.{pair.Key},@fixture_{pair.Key}::jsonb))"));
        var checkedCases = 0;
        foreach (var keyword in new string?[] { null, "Needle", "needle", "~%_[\\", "X?Z", " ? ", "%", "_", "[", "~", "\\", "Straße", "STRASSE", "é", "É", "e\u0301", "e", "İ", "i", "中文", "\t", " Needle ", " ", "never-found" })
        foreach (var pageIndex in new[] { 1, 2, 100 })
        {
            var request = new ProductSearchRequest { Keyword = keyword, PageSize = 4, PageIndex = pageIndex };
            var captured = await CaptureProductPage(request);
            var parameters = ProductPageParameters(captured.Values);
            foreach (var (table, rows) in tables) parameters.Add("fixture_" + table, JsonSerializer.Serialize(rows));
            var (countSql, rowsSql) = OriginalProductPage(captured.Sql, keyword);
            string Rewrite(string sql)
            {
                var body = Regex.Replace(sql, @"\bpublic\.(product_package|product_master|product_category|product_color|product|supplier|warehouse_site|customer_product|customer)\b", "$1");
                return body.StartsWith("WITH ", StringComparison.Ordinal) ? "WITH " + fixtures + "," + body[5..] : "WITH " + fixtures + " " + body;
            }
            var total = await connection.ExecuteScalarAsync<int>(Rewrite(countSql), parameters, transaction);
            var oldRows = (await connection.QueryAsync<ProductDto>(Rewrite(rowsSql), parameters, transaction)).AsList();
            var newTotal = -1;
            var newRows = (await connection.QueryAsync<int, ProductDto, ProductDto?>(Rewrite(captured.Sql), (count, product) =>
                { newTotal = count; return product; }, parameters, transaction, splitOn: "Uid")).OfType<ProductDto>().ToList();
            Assert.AreEqual(total, newTotal, keyword + "/" + pageIndex);
            Assert.AreEqual(JsonSerializer.Serialize(oldRows), JsonSerializer.Serialize(newRows), keyword + "/" + pageIndex);
            if (keyword == "Needle") Assert.AreEqual(10, total, "九个字段分别可命中，NULL软删除保留，已删与失效颜色排除。");
            checkedCases++;
        }
        Console.WriteLine($"只读内联夹具：{checkedCases} 组旧新查询完整 DTO、总数、顺序一致。");
        await transaction.RollbackAsync();
    }
}
