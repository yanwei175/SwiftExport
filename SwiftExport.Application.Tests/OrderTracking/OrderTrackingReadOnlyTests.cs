using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Infrastructure.Repositories.OrderTracking.Sources;
using SwiftExport.Contracts.DTOs.OrderTracking.DemandDetail;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.OrderTracking.Overview;
using SwiftExport.Infrastructure.Repositories.OrderTracking.DemandDetail;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.OrderTracking;

/// <summary>显式门禁下以只读事务执行实际仓储及内联夹具 SQL，不初始化、不清理、不写表。</summary>
[TestClass]
public sealed class OrderTrackingReadOnlyTests
{
    /// <summary>验证真实库基本读取及基础事实矩阵，所有夹具只作为 SELECT CTE 存在。</summary>
    [TestMethod]
    public async Task ReadOnlyPg_RealQueriesAndInlineFactsMatrix()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_ORDER_TRACKING_READONLY") != "1")
            Assert.Inconclusive("未启用订单跟踪只读 PG 验证。");
        var root = Root();
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "SwiftExport.WebApi", "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var target = new NpgsqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionStrings").GetProperty("Default").GetString());
        Assert.AreEqual("192.168.3.12", target.Host); Assert.AreEqual(5432, target.Port); Assert.AreEqual("dv_next", target.Database);
        target.IncludeErrorDetail = false;
        using var session = new DbSession(new PostgreSqlConnectionFactory(target.ConnectionString));
        await using var transaction = await ((NpgsqlConnection)session.Connection).BeginTransactionAsync(IsolationLevel.RepeatableRead);
        session.Transaction = transaction;
        await session.Connection.ExecuteAsync("SET TRANSACTION READ ONLY", transaction: transaction);
        Assert.AreEqual("on", await session.Connection.ExecuteScalarAsync<string>("SHOW transaction_read_only", transaction: transaction));
        var user = new OrderTrackingReadFixture.User();
        var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        var overview = new PostgreSqlOrderTrackingOverviewRepository(context); var detail = new PostgreSqlDemandTrackingHeaderRepository(context);
        var sourceRepo=new PostgreSqlOrderTrackingSourceRepository(context);
        var liveGroups=await sourceRepo.SearchSourcesAsync(new(){PageSize=3},OrderTrackingReadFixture.BusinessDate);
        var liveItems=new List<OrderTrackingListItemDto>();
        foreach(var group in liveGroups.Items)
        {
            var full=await overview.GetSourceDemandsAsync(group.SourceType,group.SourceGroupId);
            Assert.AreEqual(group.TotalDemandCount,full.Items.Count);liveItems.AddRange(full.Items);
        }
        foreach(var item in liveItems.Take(3))
        {
            var actual=await detail.GetDemandHeaderAsync(item.GrossDemandId);
            Assert.IsNotNull(actual);Assert.AreEqual(item.DemandQty,actual.DemandQty);Assert.AreEqual(item.RequiredDate,actual.RequiredDate);
            Assert.AreEqual(item.Unit,actual.Unit);Assert.AreEqual(item.Sku,actual.Sku);
        }
        _ = await overview.GetCustomerCodesAsync();

        var customerA = Guid.NewGuid(); var customerB = Guid.NewGuid(); var deletedCustomer = Guid.NewGuid();
        var sales = Guid.NewGuid(); var deletedSales = Guid.NewGuid();
        var ids = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        var rows = new[]
        {
            Gross(ids[0], "SO", sales, customerA, "2026-10-19T23:59:59", 0, null, " 原产品 ", false),
            Gross(ids[1], "SO", Guid.NewGuid(), customerA, null, 8, "PCS", "缺失来源", false),
            Gross(ids[2], "CR", sales, customerB, "2026-10-20T00:00:00", 0, "KG", "售后快照", false),
            Gross(ids[3], "MD", Guid.NewGuid(), null, "2026-10-19T00:00:00", 9, "", "手工快照", false),
            Gross(ids[4], "SO", deletedSales, customerB, "2026-10-19T05:00:00", 0, "PCS", "删除来源", false),
            Gross(ids[5], "SO", sales, deletedCustomer, "2026-10-18T23:59:59", 0, "PCS", "删除客户", false),
            Gross(ids[6], "ZZ", sales, customerA, "2026-10-19T00:00:00", 0, "PCS", "不支持", false),
            Gross(ids[7], "SO", sales, customerA, "2026-10-19T00:00:00", 0, "PCS", "软删", true)
        };
        var customers = new object[]
        {
            new { uid=customerA, customer_code="Alpha", customer_name="客户A", is_deleted=false, is_active=false },
            new { uid=customerB, customer_code="Beta", customer_name="客户B", is_deleted=false, is_active=true },
            new { uid=deletedCustomer, customer_code="Gone", customer_name="已删", is_deleted=true, is_active=true }
        };
        var heads = new object[]
        {
            new { uid=sales, customer_po_no="PO%_? ~", invoice_no="INV1", is_deleted=false },
            new { uid=deletedSales, customer_po_no="不能显示", invoice_no="不能显示", is_deleted=true }
        };
        var replay = new InlineFacts(JsonSerializer.Serialize(rows), JsonSerializer.Serialize(customers), JsonSerializer.Serialize(heads));
        var all = await Page(new() { SourceTypes = [] });
        Assert.AreEqual(6, all.TotalCount); Assert.AreEqual(6, all.Items.Select(item => item.GrossDemandId).Distinct().Count());
        var a = all.Items.Single(item => item.GrossDemandId == ids[0]);
        Assert.IsNull(a.Unit); Assert.AreEqual(" 原产品 ", a.Sku); Assert.AreEqual("PO%_? ~", a.CustomerPo);
        var missing = all.Items.Single(item => item.GrossDemandId == ids[1]);
        Assert.IsNull(missing.CustomerPo); Assert.IsNull(missing.RequiredDate); Assert.AreEqual("Alpha", missing.CustomerCode);
        Assert.IsNull(all.Items.Single(item => item.GrossDemandId == ids[2]).CustomerPo);
        Assert.IsNull(all.Items.Single(item => item.GrossDemandId == ids[3]).CustomerId);
        Assert.IsNull(all.Items.Single(item => item.GrossDemandId == ids[4]).InvoiceNo);
        Assert.IsNull(all.Items.Single(item => item.GrossDemandId == ids[5]).CustomerCode);
        Assert.AreEqual(4, (await Page(new())).TotalCount);
        Assert.AreEqual(2, (await Page(new() { SourceTypes = ["CR", "MD"] })).TotalCount);
        Assert.AreEqual(1, (await Page(new() { SourceTypes = [], LifecycleStatuses = [GrossDemandLifecycleStatusEnum.Closed] })).TotalCount);
        Assert.AreEqual(1, (await Page(new() { SourceTypes = [], LifecycleStatuses = [GrossDemandLifecycleStatusEnum.Voided] })).TotalCount);
        var dated = await Page(new() { SourceTypes = [], RequiredDateFrom = new(2026, 10, 19, 12, 0, 0), RequiredDateTo = new(2026, 10, 19, 12, 0, 0) });
        Assert.AreEqual(3,dated.TotalCount);
        CollectionAssert.AreEquivalent(new[] { ids[0],ids[3],ids[4],ids[5] },dated.Items.Select(item=>item.GrossDemandId).ToArray());
        Assert.AreEqual(5, (await Page(new() { SourceTypes = [], RequiredDateTo = DateTime.MaxValue })).TotalCount);
        var multi = await Page(new() { SourceTypes = [], CustomerCodes = [" alpha ", "BETA"] });
        Assert.AreEqual(4, multi.TotalCount);
        Assert.AreEqual(2, (await Page(new() { SourceTypes = [], Keyword = "PO%_? ~" })).TotalCount);
        Assert.AreEqual(1, (await Page(new() { SourceTypes = [], Keyword = "原产品" })).TotalCount);
        Assert.AreEqual(2, (await Page(new() { SourceTypes = [], Keyword = "INV1" })).TotalCount);
        Assert.AreEqual(0, (await Page(new() { SourceTypes = [], CustomerCodes = ["Beta"], Keyword = "原产品" })).TotalCount);
        Assert.AreEqual(0, (await Page(new() { SourceTypes = [], Keyword = "客户A" })).TotalCount);
        Assert.AreEqual(0, (await Page(new() { SourceTypes = [], Keyword = "原描述" })).TotalCount);
        Assert.AreEqual(1, (await Page(new() { SourceTypes = [], Keyword = ids[0].ToString() })).TotalCount);
        var first=await SourcePage(new(){SourceTypes=[],PageSize=2});
        var second=await SourcePage(new(){SourceTypes=[],PageSize=2,PageIndex=2});
        Assert.AreEqual(4,first.Items.Concat(second.Items).Select(item=>(item.SourceType,item.SourceGroupId)).Distinct().Count());
        var beyond=await SourcePage(new(){SourceTypes=[],PageSize=2,PageIndex=99});
        Assert.AreEqual(5,beyond.TotalCount);Assert.HasCount(0,beyond.Items);
        var actualDetail = await Header(ids[0]); Assert.IsNotNull(actualDetail); Assert.IsNull(actualDetail.Unit);
        Assert.AreEqual(a.DemandQty, actualDetail.DemandQty); Assert.AreEqual(a.Sku, actualDetail.Sku);
        Assert.IsNull(await Header(ids[6])); Assert.IsNull(await Header(ids[7]));
        using (var f = new OrderTrackingReadFixture())
        {
            f.Connection.Results.Enqueue(new DataTable()); await f.Overview.GetCustomerCodesAsync();
            var codes = (await session.Connection.QueryAsync<string>(new CommandDefinition(replay.Sql(f.Connection.Commands.Single().Sql), replay.Parameters(f.Connection.Commands.Single()), transaction))).ToArray();
            CollectionAssert.AreEqual(new[] { "Alpha", "Beta" }, codes);
        }
        await transaction.RollbackAsync(); session.Transaction = null;
        var evidence = new { ReadOnly=true, LiveTotal=liveItems.Count, LiveDetails=liveItems.Take(3).Count(), InlineRows=8, VisibleRows=6,
            MissingAssociationsPreserved=true, LiteralAndDateFilters=true, IndependentCustomerCodes=true };
        File.WriteAllText(Path.Combine(root, "SwiftExport.Application.Tests", "TestResults", "订单跟踪只读PG证据.json"), JsonSerializer.Serialize(evidence));

        // 捕获生产命令后只替换表来源，参数、筛选和分页仍由真实仓储及 PG 执行。
        async Task<PagedResponseBase<OrderTrackingSourceGroupDto>> SourcePage(OrderTrackingSourceSearchRequest request)
        {
            using var f=new OrderTrackingReadFixture();f.Page(0);await f.Sources.SearchSourcesAsync(request,OrderTrackingReadFixture.BusinessDate);
            var count=await session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(replay.Sql(f.Connection.Commands[0].Sql),replay.Parameters(f.Connection.Commands[0]),transaction));
            var groups=await session.Connection.QueryAsync<OrderTrackingSourceGroupDto>(new CommandDefinition(replay.Sql(f.Connection.Commands[1].Sql),replay.Parameters(f.Connection.Commands[1]),transaction));
            return new(){TotalCount=count,Items=groups.AsList()};
        }
        // 主表命中条数和全来源子表分开验证；筛选仅选组，子表不重新套筛选。
        async Task<PagedResponseBase<OrderTrackingListItemDto>> Page(OrderTrackingSourceSearchRequest request)
        {
            var groups=await SourcePage(request);var items=new List<OrderTrackingListItemDto>();
            foreach(var group in groups.Items)
            {
                using var f=new OrderTrackingReadFixture();f.Connection.Results.Enqueue(new DataTable());
                await f.Overview.GetSourceDemandsAsync(group.SourceType,group.SourceGroupId);
                var command=f.Connection.Commands.Single();
                var rows=await session.Connection.QueryAsync<OrderTrackingListItemDto>(new CommandDefinition(replay.Sql(command.Sql),replay.Parameters(command),transaction));
                items.AddRange(rows);
            }
            return new(){TotalCount=groups.Items.Sum(group=>group.MatchedDemandCount),Items=items};
        }
        // 明细独立重放公开仓储 SQL，不借用总览 DTO 或在测试中模拟身份筛选。
        async Task<DemandTrackingHeaderDto?> Header(Guid id)
        {
            using var f = new OrderTrackingReadFixture(); f.Connection.Results.Enqueue(new DataTable()); await f.Detail.GetDemandHeaderAsync(id);
            var c = f.Connection.Commands.Single();
            return await session.Connection.QuerySingleOrDefaultAsync<DemandTrackingHeaderDto>(new CommandDefinition(replay.Sql(c.Sql), replay.Parameters(c), transaction));
        }
    }

    /// <summary>生成仅内联使用的需求快照，避免任何数据库初始化或清理。</summary>
    private static object Gross(Guid id, string type, Guid source, Guid? customer, string? required, int lifecycle, string? unit, string sku, bool deleted) => new
    {
        uid=id, version=7, source_type=type, source_uid=source, source_line_id=id, source_no=id.ToString(), source_line_number=1,
        customer_id=customer, product_id=(Guid?)null, is_non_standard=true, sku_snapshot=sku, customer_sku_snapshot="客型",
        link_sku_snapshot="中性", image_path_snapshot="原图片", description_snapshot="原描述", color_snapshot="原色",
        specification_snapshot="原规格", demand_qty=12.5m, unit_snapshot=unit, required_date=required, lifecycle_status=lifecycle,
        is_deleted=deleted, created_date="2026-10-01T00:00:00", remark="备注", source_version=(int?)null
    };

    /// <summary>仅向上定位当前工作树配置，不读取其他工作树或输出连接秘密。</summary>
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SwiftExport.WebApi", "appsettings.json"))) directory=directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("未找到当前工作树配置。");
    }

    /// <summary>只将实际查询表引用替换为只读 CTE，绑定参数仍复用生产命令类型。</summary>
    private sealed class InlineFacts(string gross, string customers, string sales)
    {
        internal string Sql(string sql) => """
            WITH fixture_gross AS (SELECT * FROM jsonb_populate_recordset(NULL::public.gross_demand,@FixtureGross)),
            fixture_customers AS (SELECT * FROM jsonb_populate_recordset(NULL::public.customer,@FixtureCustomers)),
            fixture_sales AS (SELECT * FROM jsonb_populate_recordset(NULL::public.sales_order_header,@FixtureSales))
            """ + (sql.TrimStart().StartsWith("WITH ",StringComparison.OrdinalIgnoreCase)?","+sql.TrimStart()[5..]:" "+sql).Replace("public.gross_demand g", "fixture_gross g").Replace("public.customer c", "fixture_customers c").Replace("public.sales_order_header so", "fixture_sales so");
        internal SqlMapper.IDynamicParameters Parameters(OrderTrackingReadFixture.RecordedCommand command) => new Replay(command, gross, customers, sales);
        /// <summary>保持生产参数类型，仅追加 SELECT CTE 使用的 JSONB 数据。</summary>
        private sealed class Replay(OrderTrackingReadFixture.RecordedCommand command, string gross, string customers, string sales) : SqlMapper.IDynamicParameters
        {
            /// <summary>克隆当前命令参数，各查询互不共享 Npgsql 参数所有权。</summary>
            public void AddParameters(IDbCommand target, SqlMapper.Identity identity)
            {
                foreach (var parameter in command.Parameters) target.Parameters.Add(parameter.Clone());
                target.Parameters.Add(new NpgsqlParameter("FixtureGross", NpgsqlDbType.Jsonb) { Value=gross });
                target.Parameters.Add(new NpgsqlParameter("FixtureCustomers", NpgsqlDbType.Jsonb) { Value=customers });
                target.Parameters.Add(new NpgsqlParameter("FixtureSales", NpgsqlDbType.Jsonb) { Value=sales });
            }
        }
    }
}
