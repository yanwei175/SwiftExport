using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Products.Catalog;

namespace SwiftExport.Application.Tests.Database;

/// <summary>显式启用后仅在只读事务中比较物料搜索计划，不初始化或改写任何业务数据。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>同连接同快照交替测量旧新查询，比较总数、顺序和完整 DTO，保存执行计划。</summary>
    [TestMethod]
    public async Task ProductPage_ReadOnlyEquivalenceAndPerformance()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_PRODUCT_PAGE_READONLY") != "1")
            Assert.Inconclusive("未启用本轮只读性能验证。");
        await using var connection = ProductPageConnection();
        using var session = new DbSession(new ProductReadOnlyFactory(connection));
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await connection.ExecuteAsync("SET TRANSACTION READ ONLY", transaction: transaction);
        Assert.AreEqual("on", await connection.ExecuteScalarAsync<string>("SHOW transaction_read_only", transaction: transaction));
        session.Transaction = transaction;
        var user = new User();
        var context = new SwiftExport.Infrastructure.Contexts.RepositoryBaseContext(session,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            new SwiftExport.Infrastructure.Services.Auth.AuditStampService(user), user);
        var repository = new PostgreSqlProductRepository(context);
        var sample = await connection.QuerySingleAsync("""
            SELECT p.sku,p.description_zh,pc.color_code,pat.category_name,p.material_type,
                pm.category_id,pc.uid AS color_id,cp.customer_id,c.customer_code
            FROM public.product p JOIN public.product_master pm ON pm.uid=p.product_master_id AND pm.is_deleted=FALSE
            JOIN public.product_category pat ON pat.uid=pm.category_id AND pat.is_deleted=FALSE
            JOIN public.product_color pc ON pc.uid=p.color_id AND pc.is_deleted=FALSE
            JOIN public.customer_product cp ON cp.sku_id=p.uid AND cp.is_deleted=FALSE
            JOIN public.customer c ON c.uid=cp.customer_id AND c.is_deleted=FALSE
            WHERE p.is_deleted=FALSE AND p.description_zh IS NOT NULL ORDER BY p.uid LIMIT 1
            """, transaction: transaction);
        var cases = new (string Name, ProductSearchRequest Request)[]
        {
            ("无条件", new()), ("无命中chair", new() { Keyword = "chair" }),
            ("SKU命中", new() { Keyword = sample.sku }),
            ("中文描述", new() { Keyword = ((string)sample.description_zh)[..1] }),
            ("颜色", new() { Keyword = sample.color_code }), ("分类", new() { Keyword = sample.category_name }),
            ("字面转义", new() { Keyword = "~%_[\\" }), ("混合通配", new() { Keyword = " ? " }),
            ("组合筛选", new() { Keyword = sample.sku, CategoryId = sample.category_id,
                CategoryIds = [(Guid)sample.category_id], ColorIds = [(Guid)sample.color_id],
                CustomerId = sample.customer_id, CustomerIds = [(Guid)sample.customer_id],
                CustomerCodes = [(string)sample.customer_code], MaterialTypeList = [(string)sample.material_type],
                CategoryName = sample.category_name, BatchKeywords = [(string)sample.sku] }),
            ("越界页", new() { Keyword = sample.sku, PageIndex = 100000 }),
            ("第二页", new() { Keyword = "?", PageIndex = 2, PageSize = 7 })
        };
        var evidence = new List<object>();
        foreach (var (name, request) in cases)
        {
            var captured = await CaptureProductPage(request);
            var (oldCount, oldRows) = OriginalProductPage(captured.Sql, request.Keyword);
            var parameters = ProductPageParameters(captured.Values);
            var countTimes = new List<double>(); var rowsTimes = new List<double>(); var newTimes = new List<double>();
            PagedResponseBase<ProductDto>? current = null;
            for (var round = 0; round < 3; round++)
            {
                if (round % 2 == 1) await NewPage();
                var watch = Stopwatch.StartNew();
                var total = await connection.ExecuteScalarAsync<int>(oldCount, parameters, transaction);
                countTimes.Add(watch.Elapsed.TotalMilliseconds); watch.Restart();
                var rows = (await connection.QueryAsync<ProductDto>(oldRows, parameters, transaction)).AsList();
                rowsTimes.Add(watch.Elapsed.TotalMilliseconds);
                if (round % 2 == 0) await NewPage();
                Assert.AreEqual(total, current!.TotalCount, name);
                Assert.AreEqual(JsonSerializer.Serialize(rows), JsonSerializer.Serialize(current.Items), name + "完整 DTO 不一致");
                async Task NewPage()
                {
                    var timer = Stopwatch.StartNew(); current = await repository.GetPagedAsync(request);
                    newTimes.Add(timer.Elapsed.TotalMilliseconds);
                }
            }
            var plans = new Dictionary<string, JsonElement>();
            foreach (var (key, sql) in new[] { ("oldCount", oldCount), ("oldRows", oldRows), ("newPage", captured.Sql) })
            {
                var plan = await connection.ExecuteScalarAsync<string>("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + sql, parameters, transaction);
                plans[key] = JsonSerializer.Deserialize<JsonElement>(plan!);
            }
            evidence.Add(new { Case = name, Total = current!.TotalCount, PageRows = current.Items.Count,
                FullDtoEqual = true, CountMs = countTimes, RowsMs = rowsTimes, NewMs = newTimes, Plans = plans });
            Console.WriteLine($"{name}: count={countTimes.Order().ElementAt(1):F1}, rows={rowsTimes.Order().ElementAt(1):F1}, new={newTimes.Order().ElementAt(1):F1} ms; total={current.TotalCount}");
            File.WriteAllText(Path.Combine(ProductPageRoot(), "SwiftExport.Application.Tests", "TestResults", "物料分页只读性能证据.json"),
                JsonSerializer.Serialize(new { DatabaseReadOnly = true, Rounds = 3, Evidence = evidence }, new JsonSerializerOptions { WriteIndented = true }));
        }
        await transaction.RollbackAsync(); session.Transaction = null;
    }

    /// <summary>借用已进入只读事务的现有连接，不在仓储内创建连接。</summary>
    private sealed class ProductReadOnlyFactory(NpgsqlConnection connection) : IDbConnectionFactory
    {
        /// <summary>返回本例已校验目标的连接。</summary>
        public IDbConnection CreateConnection() => connection;
    }

    /// <summary>捕获生产 SQL 和参数，不连接数据库。</summary>
    private static async Task<(string Sql, Dictionary<string, object?> Values)> CaptureProductPage(ProductSearchRequest request)
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("total_count", typeof(int));
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        await new PostgreSqlProductRepository(DictionaryContext(fixture)).GetPagedAsync(request);
        var command = fixture.Connection.Commands.Single(); return (command.Sql, command.Values);
    }

    /// <summary>恢复原九字段 OR、宽投影与包装后 LIMIT；其他参数化筛选从生产 SQL 捕获。</summary>
    private static (string Count, string Rows) OriginalProductPage(string sql, string? keyword)
    {
        sql = sql.Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = sql.IndexOf("FROM public.product p", StringComparison.Ordinal);
        var end = sql.IndexOf(string.IsNullOrWhiteSpace(keyword) ? ")\nSELECT totals" : "),\nsearch_texts", start, StringComparison.Ordinal);
        var fromWhere = sql[start..end];
        if (!string.IsNullOrWhiteSpace(keyword))
            fromWhere += " AND (" + string.Join(" OR ", new[] { "p.sku", "p.description_en", "p.description_zh", "p.features_en", "p.features_zh", "pc.color_code", "pc.color_name_en", "pc.color_name_zh", "pat.category_name" }.Select(Like)) + ")";
        var whereIndex = fromWhere.IndexOf("WHERE ", StringComparison.Ordinal);
        var from = fromWhere[..whereIndex]; var where = fromWhere[whereIndex..];
        return ("SELECT count(*) " + fromWhere, $"""
            SELECT {ProductPageConstant("CatalogDtoColumns")},{ProductPageConstant("CatalogPagedClassificationColumns")},{ProductPageConstant("CatalogReferenceColumns")},pc.color_name_zh AS "Finish",
                pat.fake_name AS "FakeName",pc.color_code_unique AS "ColorCodeUnique",pc.color_code AS "ColorCode",pc.color_name_en AS "ColorNameEn",
                pkg.pcs_per_package AS "PcsOfCarton",pkg.nw_kg AS "NwOfCarton",pkg.gw_kg AS "GwOfCarton",pkg.cbm AS "CbmOfCarton"
            {from} LEFT JOIN public.product_package pkg ON pkg.owner_product_id=p.uid
                AND pkg.package_type=@CartonPackageType AND pkg.is_deleted=FALSE
            {where} ORDER BY p.sku COLLATE public.erp_dictionary_ci ASC NULLS FIRST,p.uid LIMIT @Limit OFFSET @Offset
            """);
    }

    /// <summary>恢复 UUID/text 数组的显式参数类型，与仓储绑定一致。</summary>
    private static PostgreSqlParameters ProductPageParameters(Dictionary<string, object?> values)
    {
        var parameters = new PostgreSqlParameters();
        foreach (var (name, value) in values)
            if (value is Guid[] ids) parameters.AddIds(name, ids);
            else if (value is string[] texts) parameters.AddTexts(name, texts);
            else parameters.Add(name, value);
        return parameters;
    }

    /// <summary>保持现有 ICU 包含匹配和转义，不改变大小写、重音或 Unicode 口径。</summary>
    private static string Like(string column) => $"{column} COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'";

    /// <summary>读取仓储投影常量用于原 SQL 基线，不改变生产可见 API。</summary>
    private static string ProductPageConstant(string name) => (string)typeof(PostgreSqlProductRepository).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;

    /// <summary>仅复用本工作树配置，固定本轮授权实例/库且不输出凭据。</summary>
    private static NpgsqlConnection ProductPageConnection()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProductPageRoot(), "SwiftExport.WebApi", "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var builder = new NpgsqlConnectionStringBuilder(json.RootElement.GetProperty("ConnectionStrings").GetProperty("Default").GetString());
        Assert.AreEqual("192.168.3.12", builder.Host); Assert.AreEqual(5432, builder.Port); Assert.AreEqual("dv_next", builder.Database);
        builder.CommandTimeout = 60; builder.IncludeErrorDetail = false;
        return new NpgsqlConnection(builder.ConnectionString);
    }

    /// <summary>从测试输出目录定位当前工作树，避免读取其他窗口配置。</summary>
    private static string ProductPageRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SwiftExport-WebAPI.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
