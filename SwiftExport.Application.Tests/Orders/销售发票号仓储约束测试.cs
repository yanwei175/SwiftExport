using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Common;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.SalesOrders.Document;

namespace SwiftExport.Application.Tests.Orders;

/// <summary>核对发票查询、元数据索引及特定约束异常分类，不能替代 PostgreSQL 实库验证。</summary>
[TestClass]
public sealed class 销售发票号仓储约束测试
{
    /// <summary>查询不限制客户或单据状态，更新排除自身，部分索引与比较口径对应。</summary>
    [TestMethod]
    public void QueryAndIndexCoverAllUndeletedOrders()
    {
        var sql = (string)typeof(PostgreSqlSalesOrderHeadRepository).GetField("InvoiceLookupSql", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        StringAssert.Contains(sql, "is_deleted=FALSE AND uid<>@ExcludeUid");
        StringAssert.Contains(sql, "btrim(invoice_no,' ') COLLATE public.erp_dictionary_ci");
        StringAssert.Contains(sql, "btrim(@InvoiceNo,' ') COLLATE public.erp_dictionary_ci");
        Assert.IsFalse(sql.Contains("customer_id", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("confirmation_status", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("document_status", StringComparison.Ordinal));
        var schema = new PostgreSqlSchemaGenerator().Generate(typeof(BaseEntity).Assembly);
        var index = schema.Split('\n').Single(line => line.StartsWith("CREATE UNIQUE INDEX \"ux_sales_order_header_invoice_no\"", StringComparison.Ordinal));
        StringAssert.Contains(index, "(btrim(\"invoice_no\", ' ') COLLATE public.erp_dictionary_ci)");
        StringAssert.Contains(index, "WHERE \"is_deleted\" = FALSE AND \"invoice_no\" IS NOT NULL AND btrim(\"invoice_no\", ' ') <> ''");
    }

    /// <summary>仅发票索引的 23505 为发票重复，不吞掉其他唯一冲突或数据库错误。</summary>
    [TestMethod]
    [DataRow("23505", "ux_sales_order_header_invoice_no", true)]
    [DataRow("23505", "ux_sales_order_header_order_no", false)]
    [DataRow("23514", "ux_sales_order_header_invoice_no", false)]
    public void OnlyInvoiceUniqueViolationIsConverted(string sqlState, string constraint, bool expected)
    {
        var exception = new PostgresException("不分析消息", "ERROR", "ERROR", sqlState, constraintName: constraint);
        var method = typeof(PostgreSqlSalesOrderHeadRepository).GetMethod("IsInvoiceConflict", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.AreEqual(expected, method.Invoke(null, [exception]));
    }
}
