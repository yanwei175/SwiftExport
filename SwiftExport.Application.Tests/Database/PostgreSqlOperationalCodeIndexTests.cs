using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Common;
using SwiftExport.Infrastructure.DatabaseSchema;

namespace SwiftExport.Application.Tests.Database;

/// <summary>验证编码及来源唯一键的常用比较、NULL和活动范围边界；实库只使用专用测试库。</summary>
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlOperationalCodeIndexTests
{

    /// <summary>实际来源唯一键与查询共用大小写及尾普通空格规则，仍允许不同来源、行及非活动范围；全部新行回滚。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlOperationalCodeIntegration")]
    public void GrossSource_UniquenessMatchesQueryComparison()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var source = Guid.NewGuid();
        var line = Guid.NewGuid();
        var ids = new List<Guid>();
        Insert("SO");
        foreach (var variant in new[] { "so", "SO " })
        {
            transaction.Save("source_key_probe");
            try
            {
                var error = Assert.ThrowsExactly<PostgresException>(() => Insert(variant));
                Assert.AreEqual(PostgresErrorCodes.UniqueViolation, error.SqlState);
                Assert.AreEqual("ux_gross_demand_source_line", error.ConstraintName);
            }
            finally
            {
                // 失败和旧索引放过重复的复现都只回滚本保存点，最终事务仍完整回滚。
                transaction.Rollback("source_key_probe");
            }
        }
        Insert("MD");
        Insert(" SO");
        Insert("SO", otherSource: Guid.NewGuid());
        Insert("SO", otherLine: Guid.NewGuid());
        Insert("SO", deleted: true);
        Insert("SO", lifecycle: 8);
        Assert.AreEqual(1, connection.ExecuteScalar<int>("""
            SELECT count(*) FROM public.gross_demand
            WHERE NOT is_deleted AND lifecycle_status=0 AND source_uid=@Source AND source_line_id=@Line
              AND rtrim(source_type,' ') COLLATE public.erp_dictionary_ci =
                  rtrim('so ',' ') COLLATE public.erp_dictionary_ci
            """, new { Source = source, Line = line }, transaction));
        transaction.Rollback();
        Assert.AreEqual(0, connection.ExecuteScalar<int>(
            "SELECT count(*) FROM public.gross_demand WHERE uid=ANY(@Ids)", new { Ids = ids.ToArray() }));

        // 约束探针只插合法非标来源的必要列，不借用或修改已有业务身份、数据和号段。
        void Insert(string sourceType, bool deleted = false, int lifecycle = 0, Guid? otherSource = null, Guid? otherLine = null)
        {
            var uid = Guid.NewGuid();
            ids.Add(uid);
            connection.Execute("""
                INSERT INTO public.gross_demand
                    (uid,source_type,source_uid,source_no,source_line_id,is_non_standard,sku_snapshot,demand_qty,lifecycle_status,is_deleted)
                VALUES(@Uid,@SourceType,@Source,'PG来源索引事务探针',@Line,TRUE,'PG来源索引事务探针',1,@Lifecycle,@Deleted)
                """, new { Uid = uid, SourceType = sourceType, Source = otherSource ?? source,
                    Line = otherLine ?? line, Lifecycle = lifecycle, Deleted = deleted }, transaction, commandTimeout: 10);
        }
    }

    /// <summary>核对保留的用户及仓库编码索引；客户采购配置已改为双方身份唯一，另有专项验证。</summary>
    [TestMethod]
    [DataRow("sys_user", "ux_sys_user_user_name", "user_name", null)]
    [DataRow("warehouse_site", "ux_warehouse_site_code", "code", null)]
    public void Schema_KeepsColumnAndNullBoundaries(string table, string index, string key, string? second)
    {
        var script = new PostgreSqlSchemaGenerator().Generate(typeof(BaseEntity).Assembly);
        var columns = $"(rtrim(\"{key}\", ' ') COLLATE public.erp_dictionary_ci)";
        if (second is not null)
            columns += $", (rtrim(\"{second}\", ' ') COLLATE public.erp_dictionary_ci)";
        var nulls = second is null ? "" : " NULLS NOT DISTINCT";
        StringAssert.Contains(script,
            $"CREATE UNIQUE INDEX \"{index}\" ON \"public\".\"{table}\" ({columns}){nulls} WHERE \"is_deleted\" = FALSE;");
        var start = script.IndexOf($"CREATE TABLE \"public\".\"{table}\"", StringComparison.Ordinal);
        var end = script.IndexOf("\n);", start, StringComparison.Ordinal);
        Assert.IsFalse(script[start..end].Contains("COLLATE", StringComparison.Ordinal));
        StringAssert.Contains(script, "CREATE UNIQUE INDEX \"ux_warehouse_site_active_supplier\" ON \"public\".\"warehouse_site\" (\"supplier_id\", \"site_type\")");
    }

    /// <summary>大小写与尾空格冲突，重音和前导空格有区别；软删及失败更新保留原有边界。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlOperationalCodeIntegration")]
    [DataRow("sys_user", "case")]
    [DataRow("sys_user", "trailing")]
    [DataRow("sys_user", "accent")]
    [DataRow("sys_user", "leading")]
    [DataRow("sys_user", "deleted")]
    [DataRow("sys_user", "update")]
    [DataRow("warehouse_site", "case")]
    [DataRow("warehouse_site", "trailing")]
    [DataRow("warehouse_site", "accent")]
    [DataRow("warehouse_site", "leading")]
    [DataRow("warehouse_site", "deleted")]
    [DataRow("warehouse_site", "update")]
    public void UniqueIndex_PreservesComparisonAndRollback(string table, string scenario)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var marker = "__pgcode_" + Guid.NewGuid().ToString("N") + "e";
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        Assert.AreEqual(1, Insert(connection, transaction, table, first, marker, marker, scenario == "deleted"));
        var variant = scenario switch
        {
            "case" => marker.ToUpperInvariant(),
            "trailing" => marker + " ",
            "accent" => marker[..^1] + "é",
            "leading" => " " + marker,
            _ => marker
        };
        if (scenario == "update")
            Assert.AreEqual(1, Insert(connection, transaction, table, second, marker + "_other", marker + "_other"));
        transaction.Save("probe");
        PostgresException? conflict = null;
        try
        {
            if (scenario == "update")
            {
                var key = Key(table);
                // PO 必须同时碰撞两个键；只改客户码仍是合法的另一供应商组合。
                var supplierKey = table == "customer_po_config" ? ", supplier_code=@Code" : "";
                connection.Execute($"UPDATE public.{table} SET {key}=@Code{supplierKey}, version=version+1 WHERE uid=@Uid",
                    new { Code = marker, Uid = second }, transaction);
            }
            else
                Insert(connection, transaction, table, second, variant, variant);
        }
        catch (PostgresException exception) { conflict = exception; }
        transaction.Rollback("probe");
        if (scenario is "case" or "trailing" or "update")
        {
            Assert.IsNotNull(conflict);
            Assert.AreEqual(PostgresErrorCodes.UniqueViolation, conflict.SqlState);
            Assert.AreEqual(Index(table), conflict.ConstraintName);
        }
        else Assert.IsNull(conflict);
        if (scenario == "update")
            Assert.AreEqual(1, connection.ExecuteScalar<int>($"SELECT version FROM public.{table} WHERE uid=@Uid",
                new { Uid = second }, transaction));
        transaction.Rollback();
        Assert.AreEqual(0, connection.ExecuteScalar<int>($"SELECT count(*) FROM public.{table} WHERE uid=ANY(@Ids)",
            new { Ids = new[] { first, second } }));
    }

    /// <summary>客户采购配置活动记录必须有非空双方身份，旧编码空组合不能绕过新身份约束。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlOperationalCodeIntegration")]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void PoPair_NullsRequireValidParties(bool nullCustomer, bool nullSupplier)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var marker = "__pgcode_" + Guid.NewGuid().ToString("N");
        var error = Assert.ThrowsExactly<PostgresException>(() =>
            Insert(connection, transaction, "customer_po_config", Guid.NewGuid(), nullCustomer ? null : marker, nullSupplier ? null : marker));
        Assert.AreEqual(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.AreEqual("ck_customer_po_config_parties", error.ConstraintName);
        transaction.Rollback();
    }
    /// <summary>双连接按大小写与尾空格等价键等待，先提交者胜出；最后只清理本例随机身份。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlOperationalCodeIntegration")]
    [DataRow("sys_user")]
    [DataRow("warehouse_site")]
    public async Task UniqueIndex_ConcurrentEquivalentCodesHaveOneWinner(string table)
    {
        using var first = Open();
        using var second = Open();
        using var observer = Open();
        using var transaction = first.BeginTransaction();
        var marker = "__pgcode_" + Guid.NewGuid().ToString("N");
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var pid = second.ExecuteScalar<int>("SELECT pg_backend_pid()");
        Task<PostgresException?>? write = null;
        var committed = false;
        try
        {
            Insert(first, transaction, table, firstId, marker, marker);
            write = Task.Run(() =>
            {
                try
                {
                    Insert(second, null, table, secondId, marker.ToUpperInvariant() + " ", marker.ToUpperInvariant() + " ");
                    return (PostgresException?)null;
                }
                catch (PostgresException error) { return error; }
            });
            var deadline = DateTime.UtcNow.AddSeconds(4);
            var waiting = false;
            while (DateTime.UtcNow < deadline)
            {
                waiting = observer.ExecuteScalar<bool>(
                    "SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid=@Pid AND wait_event_type='Lock')",
                    new { Pid = pid });
                if (waiting) break;
                await Task.Delay(20);
            }
            Assert.IsTrue(waiting, "必须观察到真实唯一键等待，而非仅检查最终行数。");
            transaction.Commit();
            committed = true;
            var error = await write;
            Assert.IsNotNull(error);
            Assert.AreEqual(PostgresErrorCodes.UniqueViolation, error.SqlState);
            Assert.AreEqual(Index(table), error.ConstraintName);
        }
        finally
        {
            if (!committed) transaction.Rollback();
            if (write is not null) await write;
            // 三张可变基础表无不可变保护；精确 UID 清理不接触既有记录或触发器。
            observer.Execute($"DELETE FROM public.{table} WHERE uid=ANY(@Ids)",
                new { Ids = new[] { firstId, secondId } });
        }
    }

    /// <summary>读取专用库配置并打开连接，目标校验失败时不执行任何 SQL。</summary>
    private static NpgsqlConnection Open()
    {
        var connection = new NpgsqlConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString());
        connection.Open();
        return connection;
    }

    /// <summary>限定三个固定测试表，参数化新增随机记录；不读取或输出真实密码字段。</summary>
    private static int Insert(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string table, Guid uid, string? code, string? supplier, bool deleted = false)
    {
        var sql = table switch
        {
            "sys_user" => "INSERT INTO public.sys_user(uid,user_name,password_hash,credential_version,is_active,is_deleted) VALUES(@Uid,@Code,'test-only-not-a-password',1,TRUE,@Deleted)",
            "warehouse_site" => "INSERT INTO public.warehouse_site(uid,code,name,site_type,is_active,is_deleted) VALUES(@Uid,@Code,'本例随机仓库',0,TRUE,@Deleted)",
            "customer_po_config" => "INSERT INTO public.customer_po_config(uid,customer_code,supplier_code,is_deleted) VALUES(@Uid,@Code,@Supplier,@Deleted)",
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
        return connection.Execute(sql, new { Uid = uid, Code = code, Supplier = supplier, Deleted = deleted },
            transaction, commandTimeout: 10);
    }

    /// <summary>固定索引名用于验证确切约束，不把其他 23505 误报成编码冲突。</summary>
    private static string Index(string table) => table switch
    {
        "sys_user" => "ux_sys_user_user_name",
        "warehouse_site" => "ux_warehouse_site_code",
        "customer_po_config" => "ux_customer_po_config_codes",
        _ => throw new ArgumentOutOfRangeException(nameof(table))
    };

    /// <summary>固定更新列仅覆盖用户、仓库和 PO 编码，避免动态标识符来自用户输入。</summary>
    private static string Key(string table) => table switch
    {
        "sys_user" => "user_name",
        "warehouse_site" => "code",
        "customer_po_config" => "customer_code",
        _ => throw new ArgumentOutOfRangeException(nameof(table))
    };
}
