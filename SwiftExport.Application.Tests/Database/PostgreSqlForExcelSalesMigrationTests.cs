using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Infrastructure.Repositories.ForExcel;

namespace SwiftExport.Application.Tests.Database;

/// <summary>独立销售导入离线用例，以真实 Dapper 验证参数、投影及可空事务；不改写共享记录夹具。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>客户输入按 .NET 修剪及大写，数据库只修剪 ASCII 空格；真实映射保留原文、空名称及停用状态。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ExcelSales_CustomersPreserveMappingNormalizationAndOptionalTransaction(bool transaction)
    {
        using var f = new Fixture(); if (!transaction) f.Session.Transaction = null;
        f.Connection.Rows.Columns.Add("Uid", typeof(Guid)); f.Connection.Rows.Columns.Add("CustomerCode", typeof(string));
        f.Connection.Rows.Columns.Add("CustomerName", typeof(string)); f.Connection.Rows.Columns.Add("IsActive", typeof(bool));
        var uid = Guid.NewGuid(); f.Connection.Rows.Rows.Add(uid, " a%_[~ ", DBNull.Value, false);
        var rows = await new PostgreSqlForExcelSalesOrderRepository(DictionaryContext(f)).FindCustomersAsync("\t a%_[~\u3000", default);
        var row = rows.Single(); Assert.AreEqual(uid, row.Uid); Assert.AreEqual(" a%_[~ ", row.CustomerCode); Assert.IsNull(row.CustomerName); Assert.IsFalse(row.IsActive);
        Assert.AreEqual("A%_[~", f.Connection.Values["Code"]); Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
        StringAssert.Contains(f.Connection.Text, "upper(btrim(customer_code,' ')) COLLATE public.erp_dictionary_ci = @Code");
        StringAssert.Contains(f.Connection.Text, "is_deleted=FALSE");
        Assert.IsFalse(f.Connection.Text.Contains("is_active=TRUE", StringComparison.Ordinal));
        Assert.HasCount(1, f.Connection.Commands); Assert.AreEqual(0, f.Connection.Commits); Assert.AreEqual(0, f.Connection.Rollbacks);
    }

    /// <summary>型号数组作为单个 text[] 参数，保留大小写、空白、符号和重复项；仅查询客户范围且不新增资格或锁。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ExcelSales_ProductArrayIsTypedAndNotRenormalized(bool transaction)
    {
        using var f = new Fixture(); if (!transaction) f.Session.Transaction = null;
        var customer = Guid.NewGuid(); var input = new[] { " a%_[~ ", "A", "A", "\t", null! }; var original = input.ToArray();
        await new PostgreSqlForExcelSalesOrderRepository(DictionaryContext(f)).MatchProductsAsync(customer, input, default);
        Assert.AreEqual(customer, f.Connection.Values["CustomerId"]);
        CollectionAssert.AreEqual(original, (string[])f.Connection.Values["Skus"]!); CollectionAssert.AreEqual(original, input);
        Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Text, f.Connection.Types["Skus"]);
        StringAssert.Contains(f.Connection.Text, "= ANY(");
        StringAssert.Contains(f.Connection.Text, "SELECT rtrim(input.sku,' ') FROM unnest(@Skus) input(sku)");
        Assert.IsFalse(f.Connection.Text.Contains("btrim(input.sku", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Text.Contains("upper(input.sku", StringComparison.Ordinal));
        StringAssert.Contains(f.Connection.Text, "cp.customer_id=@CustomerId");
        StringAssert.Contains(f.Connection.Text, "LEFT JOIN public.product p ON p.uid=cp.sku_id AND p.is_deleted=FALSE");
        StringAssert.Contains(f.Connection.Text, "COALESCE(NULLIF(btrim(cp.link_sku,' '),''),p.link_sku)");
        StringAssert.Contains(f.Connection.Text, "NULLIF(btrim(p.unit,' '),'') IS NOT NULL");
        Assert.IsFalse(f.Connection.Text.Contains("supply_type", StringComparison.Ordinal)); Assert.IsFalse(f.Connection.Text.Contains("material_type", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Text.Contains("FOR UPDATE", StringComparison.Ordinal)); Assert.IsFalse(f.Connection.Text.Contains("HOLDLOCK", StringComparison.Ordinal));
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction); Assert.HasCount(1, f.Connection.Commands);
    }

    /// <summary>真实 Dapper 保留停用及断链 DTO、nullable 主表字段和多候选，不因 IsUsable=false 丢弃行。</summary>
    [TestMethod]
    public async Task ExcelSales_ProductProjectionPreservesUnavailableAndBrokenCandidates()
    {
        using var f = new Fixture();
        f.Connection.Rows.Columns.Add("CustomerProductId", typeof(Guid)); f.Connection.Rows.Columns.Add("ProductId", typeof(Guid));
        foreach (var name in new[] { "CustomerSku", "Sku", "LinkSku", "Unit", "MainImage" }) f.Connection.Rows.Columns.Add(name, typeof(string));
        f.Connection.Rows.Columns.Add("IsUsable", typeof(bool));
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var product = Guid.NewGuid();
        f.Connection.Rows.Rows.Add(first, product, "AQ", "REAL", "客户型号", " PCS ", "图片", false);
        f.Connection.Rows.Rows.Add(second, DBNull.Value, "AQ", DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, false);
        var rows = await new PostgreSqlForExcelSalesOrderRepository(DictionaryContext(f)).MatchProductsAsync(Guid.NewGuid(), ["AQ"], default);
        Assert.HasCount(2, rows); Assert.AreEqual(first, rows[0].CustomerProductId); Assert.AreEqual(product, rows[0].ProductId);
        Assert.AreEqual("REAL", rows[0].Sku); Assert.AreEqual("客户型号", rows[0].LinkSku); Assert.AreEqual(" PCS ", rows[0].Unit); Assert.AreEqual("图片", rows[0].MainImage);
        Assert.AreEqual(second, rows[1].CustomerProductId); Assert.IsNull(rows[1].ProductId); Assert.IsNull(rows[1].Sku); Assert.IsNull(rows[1].LinkSku);
        Assert.IsNull(rows[1].Unit); Assert.IsNull(rows[1].MainImage); Assert.IsTrue(rows.All(row => !row.IsUsable));
    }

    /// <summary>商业编号各自规范化，任一非空编号 OR 匹配；真实映射保留全部状态和 NULL，不将编号视作唯一键。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ExcelSales_ExistingOrdersKeepOrScopeNullsAndStatus(bool transaction)
    {
        using var f = new Fixture(); if (!transaction) f.Session.Transaction = null;
        f.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        foreach (var name in new[] { "OrderNo", "InvoiceNo", "CustomerPoNo" }) f.Connection.Rows.Columns.Add(name, typeof(string));
        f.Connection.Rows.Columns.Add("DocumentStatus", typeof(int)); f.Connection.Rows.Columns.Add("ConfirmationStatus", typeof(int));
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var customer = Guid.NewGuid();
        f.Connection.Rows.Rows.Add(first, "SO1", "INV", DBNull.Value, 2, 1); f.Connection.Rows.Rows.Add(second, "SO2", DBNull.Value, "PO", 0, 0);
        var rows = await new PostgreSqlForExcelSalesOrderRepository(DictionaryContext(f)).FindExistingOrdersAsync(customer, "\t inv%_[~\u3000", " po ", default);
        Assert.HasCount(2, rows); Assert.AreEqual(first, rows[0].Uid); Assert.AreEqual("SO1", rows[0].OrderNo); Assert.AreEqual("INV", rows[0].InvoiceNo);
        Assert.IsNull(rows[0].CustomerPoNo); Assert.AreEqual(2, rows[0].DocumentStatus); Assert.AreEqual(1, rows[0].ConfirmationStatus);
        Assert.AreEqual(second, rows[1].Uid); Assert.IsNull(rows[1].InvoiceNo); Assert.AreEqual("PO", rows[1].CustomerPoNo);
        Assert.AreEqual("INV%_[~", f.Connection.Values["InvoiceNo"]); Assert.AreEqual("PO", f.Connection.Values["CustomerPoNo"]); Assert.AreEqual(customer, f.Connection.Values["CustomerId"]);
        StringAssert.Contains(f.Connection.Text, "@InvoiceNo<>''"); StringAssert.Contains(f.Connection.Text, "OR (@CustomerPoNo<>''");
        StringAssert.Contains(f.Connection.Text, "is_deleted=FALSE AND customer_id=@CustomerId");
        Assert.IsFalse(f.Connection.Text.Contains("document_status=", StringComparison.Ordinal)); Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
    }

    /// <summary>空数组和双 .NET 空白编号在预取消下仍按源行为直接空结果，零会话查询。</summary>
    [TestMethod]
    public async Task ExcelSales_EmptyInputsShortCircuitBeforeCancellation()
    {
        using var f = new Fixture(); var repo = new PostgreSqlForExcelSalesOrderRepository(DictionaryContext(f));
        var cancelled = new CancellationToken(true);
        Assert.HasCount(0, await repo.MatchProductsAsync(Guid.Empty, [], cancelled));
        foreach (var value in new string?[] { null, "", "\t\u3000\u00A0" })
            Assert.HasCount(0, await repo.FindExistingOrdersAsync(Guid.Empty, value!, value!, cancelled));
        Assert.HasCount(0, f.Connection.Commands);
    }

    /// <summary>非空请求三个入口预取消均抛出取消且不查询；没有要求外层事务，仓储无隐式事务副作用。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ExcelSales_NonemptyCancellationDoesNotQuery(bool transaction)
    {
        using var f = new Fixture(); if (!transaction) f.Session.Transaction = null;
        var repo = new PostgreSqlForExcelSalesOrderRepository(DictionaryContext(f)); var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => repo.FindCustomersAsync("C", token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repo.MatchProductsAsync(Guid.NewGuid(), ["AQ"], token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repo.FindExistingOrdersAsync(Guid.NewGuid(), "INV", "", token));
        Assert.HasCount(0, f.Connection.Commands); Assert.AreEqual(0, f.Connection.Commits); Assert.AreEqual(0, f.Connection.Rollbacks); Assert.AreEqual(0, f.Connection.TransactionDisposals);
    }

    /// <summary>单一非空编号不会让另一空编号误报，原 null 转为空参数；不会生成 LIKE 通配匹配。</summary>
    [TestMethod]
    [DataRow(true)] [DataRow(false)]
    public async Task ExcelSales_OnlyNonemptyOrderNumberParticipates(bool invoice)
    {
        using var f = new Fixture();
        await new PostgreSqlForExcelSalesOrderRepository(DictionaryContext(f)).FindExistingOrdersAsync(Guid.NewGuid(), invoice ? " inv " : null!, invoice ? null! : " po ", default);
        Assert.AreEqual(invoice ? "INV" : "", f.Connection.Values["InvoiceNo"]); Assert.AreEqual(invoice ? "" : "PO", f.Connection.Values["CustomerPoNo"]);
        Assert.IsFalse(f.Connection.Text.Contains("LIKE", StringComparison.Ordinal)); Assert.IsFalse(f.Connection.Text.Contains("SELECT *", StringComparison.Ordinal));
    }
}
