using System.Reflection;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Draft;

namespace SwiftExport.Application.Tests.Database;

/// <summary>显式启用后仅访问既有专用测试库；普通场景使用临时表，并发场景使用本次随机 schema，不碰业务表。</summary>
[TestClass]
[TestCategory("PostgreSqlIntegration")]
public sealed class 采购BONO唯一约束PG测试
{
    /// <summary>两条生产查询均采用同一占号范围：所有未删除状态、跨客户供应商、普通空格修剪和大小写比较。</summary>
    [TestMethod]
    public async Task BothLookups_UseGlobalUndeletedNumberScope()
    {
        await using var connection = new NpgsqlConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString());
        await connection.OpenAsync(); await using var transaction = await connection.BeginTransactionAsync();
        await PrepareTemporary(connection, transaction);
        await Insert(connection, transaction, "pg_temp.purchase_bono_test", "  Bo-1  ", false, (int)PurchaseOrderLifecycleStatusEnum.Voided);
        foreach (var repository in new[] { typeof(PostgreSqlProcurementOrderDocumentRepository), typeof(PostgreSqlPurchaseOrderDraftRepository) })
        {
            var query = Lookup(repository).Replace("public.purchase_order_head", "pg_temp.purchase_bono_test", StringComparison.Ordinal);
            Assert.IsTrue(await connection.ExecuteScalarAsync<bool>(query, new { BoNo = "bo-1" }, transaction));
        }
        await connection.ExecuteAsync("UPDATE purchase_bono_test SET is_deleted=TRUE", transaction: transaction);
        foreach (var repository in new[] { typeof(PostgreSqlProcurementOrderDocumentRepository), typeof(PostgreSqlPurchaseOrderDraftRepository) })
            Assert.IsFalse(await connection.ExecuteScalarAsync<bool>(Lookup(repository).Replace("public.purchase_order_head", "pg_temp.purchase_bono_test", StringComparison.Ordinal), new { BoNo = "BO-1" }, transaction));
        await transaction.RollbackAsync();
    }

    /// <summary>生产生成索引拒绝另一方或作废未删除订单的占号，允许软删除释放和多个空编号。</summary>
    [TestMethod]
    public async Task Index_RejectsDuplicatesAndAllowsDeletedOrBlankNumbers()
    {
        await using var connection = new NpgsqlConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString());
        await connection.OpenAsync(); await using var transaction = await connection.BeginTransactionAsync();
        await PrepareTemporary(connection, transaction);
        await Insert(connection, transaction, "pg_temp.purchase_bono_test", "Bo-1", false, (int)PurchaseOrderLifecycleStatusEnum.Voided);
        await connection.ExecuteAsync("SAVEPOINT duplicate_bono", transaction: transaction);
        var error = await Assert.ThrowsAsync<PostgresException>(() => Insert(connection, transaction, "pg_temp.purchase_bono_test", "  bo-1  ", false, 0));
        Assert.AreEqual(PostgresErrorCodes.UniqueViolation, error.SqlState); Assert.AreEqual("ux_purchase_order_head_bo_no", error.ConstraintName);
        await connection.ExecuteAsync("ROLLBACK TO SAVEPOINT duplicate_bono", transaction: transaction);
        await Insert(connection, transaction, "pg_temp.purchase_bono_test", "BO-1", true, 0);
        foreach (var blank in new string?[] { null, null, "", "", " ", " " })
            await Insert(connection, transaction, "pg_temp.purchase_bono_test", blank, false, 0);
        await connection.ExecuteAsync("UPDATE purchase_bono_test SET is_deleted=TRUE WHERE bo_no='Bo-1'", transaction: transaction);
        await Insert(connection, transaction, "pg_temp.purchase_bono_test", "bo-1", false, 0);
        await transaction.RollbackAsync();
    }

    /// <summary>两个连接都未查到占号时，索引仍只允许一个提交；测试只创建并删除自身随机 schema 和表。</summary>
    [TestMethod]
    public async Task ConcurrentCreates_OnlyOneCanCommitSameNumber()
    {
        var connectionString = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        var schema = "purchase_bono_test_" + Guid.NewGuid().ToString("N");
        await using var first = new NpgsqlConnection(connectionString); await using var second = new NpgsqlConnection(connectionString);
        await first.OpenAsync(); await second.OpenAsync();
        // 标识只含固定前缀和程序生成的 GUID，不接纳外部表名；清理范围严格限于这两个对象。
        var table = schema + ".purchase_order_head";
        await first.ExecuteAsync("CREATE SCHEMA " + schema);
        try
        {
            await first.ExecuteAsync("CREATE TABLE " + table + Columns);
            await first.ExecuteAsync(IndexSql().Replace("\"public\".\"purchase_order_head\"", "\"" + schema + "\".\"purchase_order_head\"", StringComparison.Ordinal));
            await using var firstTransaction = await first.BeginTransactionAsync();
            await using var secondTransaction = await second.BeginTransactionAsync();
            var lookup = Lookup(typeof(PostgreSqlProcurementOrderDocumentRepository)).Replace("public.purchase_order_head", table, StringComparison.Ordinal);
            Assert.IsFalse(await first.ExecuteScalarAsync<bool>(lookup, new { BoNo = "BO-CONCURRENT" }, firstTransaction));
            Assert.IsFalse(await second.ExecuteScalarAsync<bool>(lookup, new { BoNo = "bo-concurrent" }, secondTransaction));
            await Insert(first, firstTransaction, table, "BO-CONCURRENT", false, 0);
            var pending = Insert(second, secondTransaction, table, " bo-concurrent ", false, (int)PurchaseOrderLifecycleStatusEnum.Closed);
            await Task.Delay(150);
            Assert.IsFalse(pending.IsCompleted, "另一事务尚未提交时，重复占号应等待索引冲突结果。");
            await firstTransaction.CommitAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => pending);
            Assert.AreEqual(PostgresErrorCodes.UniqueViolation, error.SqlState); Assert.AreEqual("ux_purchase_order_head_bo_no", error.ConstraintName);
            await secondTransaction.RollbackAsync();
            Assert.AreEqual(1, await first.ExecuteScalarAsync<int>("SELECT count(*) FROM " + table));
        }
        finally
        {
            await first.ExecuteAsync("DROP TABLE IF EXISTS " + table);
            await first.ExecuteAsync("DROP SCHEMA " + schema);
        }
    }

    private const string Columns = " (uid uuid NOT NULL,bo_no text NULL,is_deleted boolean NOT NULL,customer_id uuid,supplier_id uuid,lifecycle_status integer,confirmation_status integer)";
    // 测试执行生产生成器的单个索引，不另写弱化的测试约束。
    private static string IndexSql() => new PostgreSqlSchemaGenerator().Generate(typeof(BaseEntity).Assembly).Split('\n')
        .Single(line => line.StartsWith("CREATE UNIQUE INDEX \"ux_purchase_order_head_bo_no\"", StringComparison.Ordinal));
    private static string Lookup(Type repository) => (string)repository.GetField("BoNoLookupSql", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    private static async Task PrepareTemporary(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        await connection.ExecuteAsync("CREATE TEMP TABLE purchase_bono_test" + Columns + " ON COMMIT DROP", transaction: transaction);
        await connection.ExecuteAsync(IndexSql().Replace("\"public\".\"purchase_order_head\"", "pg_temp.purchase_bono_test", StringComparison.Ordinal), transaction: transaction);
    }
    // 每次插入不同双方身份，使唯一性不依赖客户或供应商；只接受本类型内部产生的表名。
    private static Task Insert(NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string? boNo, bool deleted, int status)
        => connection.ExecuteAsync("INSERT INTO " + table + " VALUES (@Uid,@BoNo,@Deleted,@Customer,@Supplier,@Status,@Confirmation)",
            new { Uid = Guid.NewGuid(), BoNo = boNo, Deleted = deleted, Customer = Guid.NewGuid(), Supplier = Guid.NewGuid(), Status = status,
                Confirmation = (int)ConfirmationStatusEnum.已确认 }, transaction, commandTimeout: 5);
}
