using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Common;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Draft;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>核对 BONO 索引、两个创建仓储的查询和特定约束分类，不替代真实 PostgreSQL 验证。</summary>
[TestClass]
public sealed class 采购BONO仓储约束测试
{
    /// <summary>查询与索引全库覆盖未删、非空号，忽略大小写及普通首尾空格，不过滤生命周期。</summary>
    [TestMethod]
    public void QueryAndIndexShareGlobalNumberScope()
    {
        foreach (var type in new[] { typeof(PostgreSqlProcurementOrderDocumentRepository), typeof(PostgreSqlPurchaseOrderDraftRepository) })
        {
            var sql = (string)type.GetField("BoNoLookupSql", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            StringAssert.Contains(sql, "is_deleted=FALSE"); StringAssert.Contains(sql, "btrim(bo_no,' ')");
            StringAssert.Contains(sql, "btrim(@BoNo,' ') COLLATE public.erp_dictionary_ci");
            foreach (var forbidden in new[] { "customer_id", "supplier_id", "lifecycle_status", "confirmation_status" })
                Assert.IsFalse(sql.Contains(forbidden, StringComparison.Ordinal));
        }
        var index = new PostgreSqlSchemaGenerator().Generate(typeof(BaseEntity).Assembly).Split('\n')
            .Single(line => line.StartsWith("CREATE UNIQUE INDEX \"ux_purchase_order_head_bo_no\"", StringComparison.Ordinal));
        StringAssert.Contains(index, "(btrim(\"bo_no\", ' ') COLLATE public.erp_dictionary_ci)");
        StringAssert.Contains(index, "WHERE \"is_deleted\" = FALSE AND \"bo_no\" IS NOT NULL AND btrim(\"bo_no\", ' ') <> ''");
    }
    /// <summary>只把 BONO 索引的唯一冲突转换成可修正拒绝，不吞订单号冲突或其他数据库异常。</summary>
    [TestMethod]
    [DataRow("23505", "ux_purchase_order_head_bo_no", true)]
    [DataRow("23505", "ux_purchase_order_head_order_no", false)]
    [DataRow("23514", "ux_purchase_order_head_bo_no", false)]
    public void OnlyBoNoUniqueViolationIsConverted(string state, string constraint, bool expected)
    {
        var error = new PostgresException("不依赖异常消息", "ERROR", "ERROR", state, constraintName: constraint);
        foreach (var type in new[] { typeof(PostgreSqlProcurementOrderDocumentRepository), typeof(PostgreSqlPurchaseOrderDraftRepository) })
            Assert.AreEqual(expected, type.GetMethod("IsBoNoConflict", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [error]));
    }
}
