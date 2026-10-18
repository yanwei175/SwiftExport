using System.Text.Json;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace SwiftExport.Application.Tests.Database;

/// <summary>人工授权后的分阶段实库验收；显式阶段之外不运行，不连接 Legacy 库，不输出凭据。</summary>
public sealed partial class CustomerPoConfigIdentityPostgreSqlTests
{
    private const string AcceptanceDatabase = "dv_customer_po_config_tests_20261018";
    private static readonly Guid SeedConfig = Guid.Parse("44444444-4444-4444-8444-444444444444");

    /// <summary>仅执行当前明确指定的探测、建库初始化、迁移安全性或清理阶段，阶段结果持久化为无凭据证据。</summary>
    [TestMethod]
    public async Task AuthorizedAcceptanceStage()
    {
        var stage = Environment.GetEnvironmentVariable("SWIFTEXPORT_PO_CONFIG_ACCEPTANCE_STAGE");
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_PO_CONFIG_IDENTITY_APPROVED") != "1" || string.IsNullOrEmpty(stage))
            Assert.Inconclusive("没有本轮显式授权阶段，不执行实库验收。");
        if (stage is "probe" or "create")
        {
            await using var admin = ApprovedConnection("postgres"); await admin.OpenAsync();
            var server = await admin.QuerySingleAsync<ServerInfo>("SELECT host(inet_server_addr()) AS \"Host\",current_setting('port')::integer AS \"Port\",current_setting('server_version') AS \"Version\"");
            Assert.AreEqual("192.168.3.12", server.Host); Assert.AreEqual(5432, server.Port);
            var exists = await admin.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM pg_database WHERE datname=@Name)", new { Name = AcceptanceDatabase });
            SaveEvidence(stage!, new { server.Host, server.Port, server.Version, Database = AcceptanceDatabase, Exists = exists });
            Assert.IsFalse(exists, "目标专库已存在，按授权必须停止写入，不能覆盖或复用。");
            if (stage == "probe") return;
            // 名称为固定授权常量；不接受用户输入标识符，不删除或覆盖任何既有库。
            await admin.ExecuteAsync("CREATE DATABASE \"dv_customer_po_config_tests_20261018\"");
            await using var created = ApprovedConnection(AcceptanceDatabase); await created.OpenAsync();
            await SetExpectedTarget(created);
            await created.ExecuteAsync(ReadSql("SwiftExport.Application.Tests/Database/Fixtures/客户采购配置专库初始化.sql"), commandTimeout: 60);
            var snapshot = await Snapshot(created);
            Assert.AreEqual(4, snapshot.Tables); Assert.AreEqual(1, snapshot.Configs); Assert.AreEqual(1, snapshot.Customers);
            Assert.AreEqual(1, snapshot.Suppliers); Assert.AreEqual(1, snapshot.Currencies);
            SaveEvidence("initialized", snapshot);
            return;
        }
        await using var connection = ApprovedConnection(AcceptanceDatabase); await connection.OpenAsync();
        await SetExpectedTarget(connection);
        Assert.AreEqual("SwiftExport.CustomerPoConfig.20261018.FreshFixture", await connection.ExecuteScalarAsync<string>(
            "SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()"));
        if (stage == "preflight")
        {
            var baseline = await DataHash(connection);
            var checks = new List<string>();
            foreach (var badId in new[] { Guid.Empty, Guid.NewGuid() })
            {
                await connection.ExecuteAsync("UPDATE public.customer_po_config SET customer_id=@badId WHERE uid=@SeedConfig", new { badId, SeedConfig });
                await AssertMigrationPreservesInvalidFixture(connection);
                await connection.ExecuteAsync("UPDATE public.customer_po_config SET customer_id=NULL WHERE uid=@SeedConfig", new { SeedConfig });
                checks.Add(badId == Guid.Empty ? "empty-explicit-id-rejected" : "missing-explicit-id-does-not-fallback-to-code");
            }
            await connection.ExecuteAsync("UPDATE public.customer_po_config SET customer_code='UNRESOLVED-CODE' WHERE uid=@SeedConfig", new { SeedConfig });
            await AssertMigrationPreservesInvalidFixture(connection);
            await connection.ExecuteAsync("UPDATE public.customer_po_config SET customer_code='C-CONFIG' WHERE uid=@SeedConfig", new { SeedConfig });
            checks.Add("unresolvable-null-id-rejected");
            var ambiguous = Guid.NewGuid();
            await connection.ExecuteAsync("INSERT INTO public.customer(uid,is_active,customer_code,doc_relate_no,deposit_rate,lead_time_days) VALUES(@ambiguous,TRUE,'C-CONFIG','AMBIGUOUS',0,0)", new { ambiguous });
            await AssertMigrationPreservesInvalidFixture(connection);
            await connection.ExecuteAsync("DELETE FROM public.customer WHERE uid=@ambiguous", new { ambiguous });
            checks.Add("ambiguous-code-backfill-rejected");
            var duplicate = Guid.NewGuid();
            await connection.ExecuteAsync("INSERT INTO public.customer_po_config(uid,customer_id,supplier_id,customer_code,supplier_code) VALUES(@duplicate,@Customer,@Supplier,'ALIAS-C','ALIAS-S')", new { duplicate, Customer, Supplier });
            await AssertMigrationPreservesInvalidFixture(connection);
            await connection.ExecuteAsync("DELETE FROM public.customer_po_config WHERE uid=@duplicate", new { duplicate });
            checks.Add("duplicate-resolved-id-pair-rejected");
            Assert.AreEqual(baseline, await DataHash(connection), "安全性场景仅清理本例改动，恢复初始化夹具，不改变迁移基准。");
            SaveEvidence("preflight", new { Checks = checks, BaselineRestored = true, Snapshot = await Snapshot(connection) });
            return;
        }
        if (stage == "migrate")
        {
            var before = await Snapshot(connection);
            await connection.ExecuteAsync(ReadSql("SwiftExport.Infrastructure/Database/PostgreSQL客户采购配置身份唯一迁移.sql"), commandTimeout: 60);
            var after = await Snapshot(connection);
            Assert.IsFalse(after.CodesIndex); Assert.IsTrue(after.PartiesIndex);
            Assert.AreEqual(1, after.BoundConfigs); Assert.AreEqual(2, after.SeedVersion);
            Assert.AreEqual(before.Configs, after.Configs); Assert.AreEqual(4, after.Tables);
            SaveEvidence("migration", new { Before = before, After = after });
            return;
        }
        if (stage == "cleanup")
        {
            var before = await Snapshot(connection);
            Assert.AreEqual(4, before.Tables); Assert.AreEqual(1, before.Configs); Assert.AreEqual(1, before.Customers);
            Assert.AreEqual(1, before.Suppliers); Assert.AreEqual(1, before.Currencies);
            Assert.AreEqual(2, before.SeedVersion, "普通测试必须整例回滚，夹具版本仍为迁移后的二。");
            await connection.ExecuteAsync(ReadSql("SwiftExport.Application.Tests/Database/Fixtures/客户采购配置专库清理.sql"), commandTimeout: 60);
            var tables = await connection.ExecuteScalarAsync<int>(UserTableCount);
            var collation = await connection.ExecuteScalarAsync<int>("SELECT count(*)::integer FROM pg_collation c JOIN pg_namespace n ON n.oid=c.collnamespace WHERE n.nspname='public' AND c.collname='erp_dictionary_ci'");
            Assert.AreEqual(0, tables); Assert.AreEqual(0, collation);
            SaveEvidence("cleanup", new { Before = before, UserTables = tables, TestCollations = collation, DatabaseRetained = true });
            return;
        }
        Assert.Fail("未知验收阶段，未执行任何阶段写入。");
    }

    /// <summary>仅复用现有安全配置的凭据；固定实例并显式替换为系统目录或本轮专库，不打印连接内容。</summary>
    private static NpgsqlConnection ApprovedConnection(string database)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "SwiftExport.WebApi", "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var builder = new NpgsqlConnectionStringBuilder(json.RootElement.GetProperty("ConnectionStrings").GetProperty("Default").GetString());
        Assert.AreEqual("192.168.3.12", builder.Host); Assert.AreEqual(5432, builder.Port);
        Assert.IsTrue(database is "postgres" or AcceptanceDatabase);
        builder.Database = database; builder.SearchPath = "public"; builder.IncludeErrorDetail = false;
        builder.Timeout = 10; builder.CommandTimeout = 60;
        return new NpgsqlConnection(builder.ConnectionString);
    }

    /// <summary>只配置本连接的已批准预期目标，供已审查脚本再次校验。</summary>
    private static Task SetExpectedTarget(NpgsqlConnection connection) => connection.ExecuteAsync(
        "SELECT set_config('swiftexport.expected_database',@Database,FALSE),set_config('swiftexport.expected_server','192.168.3.12',FALSE),set_config('swiftexport.expected_port','5432',FALSE)", new { Database = AcceptanceDatabase });

    /// <summary>实际执行原迁移文件并要求 P0001 业务拒绝；显式回滚后验证数据和旧索引原封不动。</summary>
    private static async Task AssertMigrationPreservesInvalidFixture(NpgsqlConnection connection)
    {
        var before = await DataHash(connection);
        var exception = await Assert.ThrowsExactlyAsync<PostgresException>(() => connection.ExecuteAsync(
            ReadSql("SwiftExport.Infrastructure/Database/PostgreSQL客户采购配置身份唯一迁移.sql"), commandTimeout: 60));
        Assert.AreEqual(PostgresErrorCodes.RaiseException, exception.SqlState);
        await connection.ExecuteAsync("ROLLBACK");
        Assert.AreEqual(before, await DataHash(connection));
        var snapshot = await Snapshot(connection);
        Assert.IsTrue(snapshot.CodesIndex); Assert.IsFalse(snapshot.PartiesIndex);
    }

    private const string UserTableCount = "SELECT count(*)::integer FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND c.relkind IN ('r','p','v','m','S','f')";

    /// <summary>只返回行数、索引存在性和已知夹具版本，不输出主数据字段或连接信息。</summary>
    private static Task<DatabaseSnapshot> Snapshot(NpgsqlConnection connection) => connection.QuerySingleAsync<DatabaseSnapshot>($"""
        SELECT ({UserTableCount}) AS "Tables",(SELECT count(*) FROM public.customer) AS "Customers",
          (SELECT count(*) FROM public.supplier) AS "Suppliers",(SELECT count(*) FROM public.sys_dic_currency) AS "Currencies",
          (SELECT count(*) FROM public.customer_po_config) AS "Configs",
          (SELECT count(*) FROM public.customer_po_config WHERE customer_id IS NOT NULL AND supplier_id IS NOT NULL) AS "BoundConfigs",
          (SELECT version FROM public.customer_po_config WHERE uid=@SeedConfig) AS "SeedVersion",
          to_regclass('public.ux_customer_po_config_codes') IS NOT NULL AS "CodesIndex",
          to_regclass('public.ux_customer_po_config_parties') IS NOT NULL AS "PartiesIndex"
        """, new { SeedConfig });

    /// <summary>散列四表全字段以确认失败迁移未改写任何夹具事实，不输出行内容。</summary>
    private static Task<string?> DataHash(NpgsqlConnection connection) => connection.ExecuteScalarAsync<string>("""
        SELECT md5(jsonb_build_array(
          (SELECT jsonb_agg(to_jsonb(t) ORDER BY uid) FROM public.customer t),
          (SELECT jsonb_agg(to_jsonb(t) ORDER BY uid) FROM public.supplier t),
          (SELECT jsonb_agg(to_jsonb(t) ORDER BY uid) FROM public.customer_po_config t),
          (SELECT jsonb_agg(to_jsonb(t) ORDER BY uid) FROM public.sys_dic_currency t))::text)
        """);

    /// <summary>记录当前阶段的安全证据，跨 dotnet test 进程保留先前结果。</summary>
    private static void SaveEvidence(string stage, object result)
    {
        var file = Path.Combine(Root(), "SwiftExport.Application.Tests", "TestResults", "客户采购配置PG验收证据.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var evidence = File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(file))! : [];
        evidence[stage] = JsonSerializer.SerializeToElement(result);
        File.WriteAllText(file, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { Stage = stage, Result = result }));
    }
    /// <summary>只读取本工作树已审核的脚本。</summary>
    private static string ReadSql(string path) => File.ReadAllText(Path.Combine(Root(), path));
    /// <summary>定位当前检出根，避免读取其他窗口的配置。</summary>
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SwiftExport-WebAPI.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
    /// <summary>仅映射公开实例标识。</summary>
    private sealed class ServerInfo
    {
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public string Version { get; set; } = "";
    }
    /// <summary>四表夹具与迁移前后结构摘要。</summary>
    private sealed class DatabaseSnapshot
    {
        public int Tables { get; set; }
        public int Customers { get; set; }
        public int Suppliers { get; set; }
        public int Currencies { get; set; }
        public int Configs { get; set; }
        public int BoundConfigs { get; set; }
        public int SeedVersion { get; set; }
        public bool CodesIndex { get; set; }
        public bool PartiesIndex { get; set; }
    }
}
