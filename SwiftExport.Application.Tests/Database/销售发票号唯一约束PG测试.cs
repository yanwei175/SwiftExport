using System.Reflection;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Common;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.SalesOrders.Document;

namespace SwiftExport.Application.Tests.Database;

/// <summary>在显式启用的专用测试库仅创建会话临时表，执行真实发票查询及索引；不修改业务表或初始化测试库。</summary>
[TestClass]
[TestCategory("PostgreSqlIntegration")]
public sealed class 销售发票号唯一约束PG测试
{
    /// <summary>实际 PG 查询须忽略普通空格和大小写、排除自身、忽略软删除，并覆盖其他客户和状态的占用。</summary>
    [TestMethod]
    public async Task LookupUsesRealPostgreSqlComparisonAndSoftDeletion()
    {
        await using var connection = new NpgsqlConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString());
        await connection.OpenAsync(); await using var transaction = await connection.BeginTransactionAsync();
        await Prepare(connection, transaction);
        var uid = Guid.NewGuid();
        await connection.ExecuteAsync("INSERT INTO invoice_unique_test VALUES (@Uid,'  Inv-1  ',FALSE)", new { Uid = uid }, transaction);
        var lookup = (string)typeof(PostgreSqlSalesOrderHeadRepository).GetField("InvoiceLookupSql", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        lookup = lookup.Replace("\"public\".\"sales_order_header\"", "pg_temp.invoice_unique_test", StringComparison.Ordinal);
        Assert.IsTrue(await connection.ExecuteScalarAsync<bool>(lookup, new { InvoiceNo = "inv-1", ExcludeUid = Guid.Empty }, transaction));
        Assert.IsFalse(await connection.ExecuteScalarAsync<bool>(lookup, new { InvoiceNo = "inv-1", ExcludeUid = uid }, transaction));
        await connection.ExecuteAsync("UPDATE invoice_unique_test SET is_deleted=TRUE WHERE uid=@Uid", new { Uid = uid }, transaction);
        Assert.IsFalse(await connection.ExecuteScalarAsync<bool>(lookup, new { InvoiceNo = "inv-1", ExcludeUid = Guid.Empty }, transaction));
        await transaction.RollbackAsync();
    }

    /// <summary>实际唯一索引拒绝另一活动行，但允许软删除重复和多个空编号；异常后显式回滚保存点。</summary>
    [TestMethod]
    public async Task UniqueIndexRejectsActiveDuplicatesAndAllowsReleasedOrBlankNumbers()
    {
        await using var connection = new NpgsqlConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString());
        await connection.OpenAsync(); await using var transaction = await connection.BeginTransactionAsync();
        await Prepare(connection, transaction);
        await Insert(connection, transaction, "Inv-1", false);
        await connection.ExecuteAsync("SAVEPOINT duplicate_invoice", transaction: transaction);
        var error = await Assert.ThrowsAsync<PostgresException>(() => Insert(connection, transaction, "  inv-1  ", false));
        Assert.AreEqual(PostgresErrorCodes.UniqueViolation, error.SqlState);
        await connection.ExecuteAsync("ROLLBACK TO SAVEPOINT duplicate_invoice", transaction: transaction);
        await Insert(connection, transaction, "INV-1", true);
        foreach (var blank in new string?[] { null, null, "", "", " ", " " }) await Insert(connection, transaction, blank, false);
        await connection.ExecuteAsync("UPDATE invoice_unique_test SET is_deleted=TRUE WHERE invoice_no='Inv-1'", transaction: transaction);
        await Insert(connection, transaction, "inv-1", false);
        await transaction.RollbackAsync();
    }

    // 复用生产生成器输出的单个索引定义，仅替换测试目标表；临时对象全部随事务结束销毁。
    private static async Task Prepare(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        await connection.ExecuteAsync("CREATE TEMP TABLE invoice_unique_test (uid uuid NOT NULL,invoice_no text NULL,is_deleted boolean NOT NULL) ON COMMIT DROP", transaction: transaction);
        var schema = new PostgreSqlSchemaGenerator().Generate(typeof(BaseEntity).Assembly);
        var index = schema.Split('\n').Single(line => line.StartsWith("CREATE UNIQUE INDEX \"ux_sales_order_header_invoice_no\"", StringComparison.Ordinal));
        await connection.ExecuteAsync(index.Replace("\"public\".\"sales_order_header\"", "pg_temp.invoice_unique_test", StringComparison.Ordinal), transaction: transaction);
    }

    // 参数化写入只触及本连接临时表，不接受外部表名、库名或业务身份。
    private static Task Insert(NpgsqlConnection connection, NpgsqlTransaction transaction, string? invoice, bool deleted)
        => connection.ExecuteAsync("INSERT INTO invoice_unique_test VALUES (@Uid,@Invoice,@Deleted)",
            new { Uid = Guid.NewGuid(), Invoice = invoice, Deleted = deleted }, transaction);
}
