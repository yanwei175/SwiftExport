using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Application.Tests.OrderTracking.Overview;

/// <summary>通过公开仓储与真实 Dapper 验证分页、参数化及基础事实边界。</summary>
[TestClass]
public sealed class OrderTrackingOverviewRepositoryTests
{
    /// <summary>空页保留总数，分页归一化且极大页码不发生整数溢出。</summary>
    [TestMethod]
    [DataRow(-1, 0, 1, 20)]
    [DataRow(2, 999, 2, 200)]
    [DataRow(int.MaxValue, 20, int.MaxValue, 20)]
    public async Task Pagination_PreservesCountAndLongOffset(int index, int size, int expectedIndex, int expectedSize)
    {
        using var f = new OrderTrackingReadFixture(true); f.Page(11);
        var result = await f.Overview.SearchAsync(new() { PageIndex = index, PageSize = size });
        Assert.AreEqual(11, result.TotalCount); Assert.HasCount(0, result.Items);
        Assert.AreEqual(expectedIndex, result.PageIndex); Assert.AreEqual(expectedSize, result.PageSize);
        var command = f.Connection.Commands[1];
        Assert.AreEqual((long)(expectedIndex - 1) * expectedSize, command.Value("Offset"));
        Assert.AreEqual(expectedSize, command.Value("PageSize"));
        StringAssert.Contains(command.Sql, "g.uid ASC");
        Assert.HasCount(2, f.Connection.Commands);
        foreach (var executed in f.Connection.Commands)
        {
            Assert.AreSame(f.Session.Transaction, executed.Transaction);
            foreach (var forbidden in new[] { "net_demand", "purchase_order", "packing_list", "demand_execution_link", "public.product", "FOR UPDATE", "INSERT ", "UPDATE " })
                Assert.IsFalse(executed.Sql.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
        Assert.AreEqual(1, f.Connection.Begins); Assert.AreEqual(0, f.Connection.Commits + f.Connection.Rollbacks);
    }

    /// <summary>集合取并集且条件取交集，日期按日包含上界，文本中的通配符及问号保持字面语义。</summary>
    [TestMethod]
    public async Task Filters_BindTypedArraysAndLiteralPatterns()
    {
        using var f = new OrderTrackingReadFixture(); f.Page(0);
        var day = new DateTime(2026, 10, 19, 23, 50, 0, DateTimeKind.Utc);
        var request = new OrderTrackingSearchRequest { SourceTypes = ["SO", "CR", "SO"], CustomerCodes = [" A ", "a", "B", " "],
            Keyword = " a b?%_~';DROP ",
            LifecycleStatuses = [GrossDemandLifecycleStatusEnum.Closed, GrossDemandLifecycleStatusEnum.Active, GrossDemandLifecycleStatusEnum.Closed],
            RequiredDateFrom = day, RequiredDateTo = DateTime.MaxValue };
        await f.Overview.SearchAsync(request);
        foreach (var c in f.Connection.Commands)
        {
            CollectionAssert.AreEqual(new[] { "SO", "CR" }, (string[])c.Value("SourceTypes"));
            CollectionAssert.AreEqual(new[] { "A", "B" }, (string[])c.Value("CustomerCodes"));
            CollectionAssert.AreEqual(new[] { 8, 0 }, (int[])c.Value("LifecycleStatuses"));
            Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Integer, c.Type("LifecycleStatuses"));
            Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Text, c.Type("CustomerCodes"));
            Assert.AreEqual("%a b?~%~_~~';DROP%", c.Value("Keyword"));
            Assert.AreEqual(day.Date, c.Value("RequiredDateFrom")); Assert.AreEqual(DateTime.MaxValue.Date, c.Value("RequiredDateTo"));
            Assert.AreEqual(NpgsqlDbType.Timestamp, c.Type("RequiredDateTo"));
            StringAssert.Contains(c.Sql, "g.required_date<@RequiredDateTo + INTERVAL '1 day'");
            foreach(var column in new[]{"g.source_no","so.customer_po_no","so.invoice_no","g.sku_snapshot","g.customer_sku_snapshot","g.link_sku_snapshot"})
                StringAssert.Contains(c.Sql, column+" COLLATE public.erp_dictionary_ci LIKE @Keyword");
            Assert.IsFalse(c.Sql.Contains("g.description_snapshot COLLATE public.erp_dictionary_ci LIKE", StringComparison.Ordinal));
            Assert.IsFalse(c.Sql.Contains("c.customer_name COLLATE public.erp_dictionary_ci LIKE", StringComparison.Ordinal));
            Assert.IsFalse(c.Sql.Contains(request.Keyword, StringComparison.Ordinal));
        }
        Assert.AreEqual(" A ", request.CustomerCodes[0]); Assert.AreEqual(day, request.RequiredDateFrom);
    }

    /// <summary>未传来源默认 SO，显式空集合只受支持来源白名单限制；空状态和空客户不增筛选。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DefaultSource_AndExplicitAllRemainDistinct(bool all)
    {
        using var f = new OrderTrackingReadFixture(); f.Page(0);
        var request = new OrderTrackingSearchRequest { SourceTypes = all ? [] : null!, CustomerCodes = [" ", "\t"] };
        await f.Overview.SearchAsync(request);
        var c = f.Connection.Commands[0];
        Assert.AreEqual(!all, c.Parameters.Any(parameter => parameter.ParameterName == "SourceTypes"));
        if (!all) CollectionAssert.AreEqual(new[] { "SO" }, (string[])c.Value("SourceTypes"));
        StringAssert.Contains(c.Sql, "IN ('SO','CR','MD')");
        Assert.IsFalse(c.Sql.Contains("g.lifecycle_status=", StringComparison.Ordinal));
        Assert.IsFalse(c.Parameters.Any(parameter => parameter.ParameterName == "CustomerCodes"));
    }

    /// <summary>所有列表字段由真实 Dapper 映射，空单位和交期不从当前主数据补值。</summary>
    [TestMethod]
    public async Task Mapping_PreservesSnapshotNullsAndMissingSource()
    {
        using var f = new OrderTrackingReadFixture();
        var expected = new OrderTrackingListItemDto { GrossDemandId = Guid.NewGuid(), Version = 7, SourceType = "MD", SourceUid = Guid.NewGuid(),
            SourceLineId = Guid.NewGuid(), SourceLineNumber = null, SourceNo = "原来源", CustomerId = null, CustomerCode = null!, CustomerName = null!,
            CustomerPo = null!, InvoiceNo = null!, ProductId = null, IsNonStandard = true, Sku = "快照", CustomerSku = "客型", LinkSku = "中性",
            ImagePath = "图片", Description = "描述", Color = "红", Specification = "规格", DemandQty = 12.25m, Unit = null!, RequiredDate = null,
            LifecycleStatus = GrossDemandLifecycleStatusEnum.Voided };
        f.Page(1, expected);
        var actual = (await f.Overview.SearchAsync(new())).Items.Single();
        var sql = f.Connection.Commands[1].Sql;
        foreach (var property in typeof(OrderTrackingListItemDto).GetProperties())
        {
            Assert.AreEqual(property.GetValue(expected), property.GetValue(actual), property.Name);
            StringAssert.Contains(sql, "AS \"" + property.Name + "\"");
        }
        StringAssert.Contains(sql, "LEFT JOIN public.customer");
        StringAssert.Contains(sql, "LEFT JOIN public.sales_order_header");
        StringAssert.Contains(sql, "rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='SO'");
    }

    /// <summary>候选只取未删除需求引用客户，未要求销售权限或采购交付存在。</summary>
    [TestMethod]
    public async Task CustomerCodes_ReadOnlyDemandCustomers()
    {
        using var f = new OrderTrackingReadFixture(true);
        f.Connection.Results.Enqueue(OrderTrackingReadFixture.Table(new { CustomerCode = "A" }, new { CustomerCode = "B" }));
        CollectionAssert.AreEqual(new[] { "A", "B" }, await f.Overview.GetCustomerCodesAsync());
        var c = f.Connection.Commands.Single();
        StringAssert.Contains(c.Sql, "g.customer_id=c.uid AND g.is_deleted=FALSE");
        StringAssert.Contains(c.Sql, "GROUP BY"); StringAssert.Contains(c.Sql, "ORDER BY");
        Assert.AreSame(f.Session.Transaction, c.Transaction);
        Assert.IsFalse(c.Sql.Contains("sales_order", StringComparison.Ordinal));
    }

    /// <summary>预取消不发送 SQL，调用方事务不会由查询结束。</summary>
    [TestMethod]
    public async Task Cancellation_DoesNotStartReadOrOwnTransaction()
    {
        using var f = new OrderTrackingReadFixture(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Overview.SearchAsync(new(), new CancellationToken(true)));
        Assert.HasCount(0, f.Connection.Commands);
        Assert.AreEqual(0, f.Connection.Commits + f.Connection.Rollbacks);
    }
}
