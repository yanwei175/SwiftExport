using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace SwiftExport.Application.Tests.Database;

/// <summary>管理用户批准的单固定PG测试库；串行租用，导出本例全部事实后仅重置其public结构。</summary>
internal sealed class PostgreSqlLegacyTestDatabase : IAsyncDisposable
{
    private const string DatabaseName = "dv_next_pg_legacy_tests_20261006";
    private const string BaselineHash = "FE5481C997822F653965406420B60B0A0857BD60570927976520DD1A778959F7";
    private readonly string root;
    private readonly string baseline;
    private readonly string resetBaseline;
    private readonly string exportDirectory;
    private readonly FileStream lease;
    private readonly TestContext context;
    private bool initialized;
    private static bool resetFailed;

    /// <summary>本连接始终禁用连接池，避免例间导出和重置时留下本测试的空闲客户端。</summary>
    public string ConnectionString { get; }

    private PostgreSqlLegacyTestDatabase(TestContext context)
    {
        this.context = context;
        root = FindRoot();
        var configured = Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_LEGACY_TEST_CONNECTION");
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_LEGACY_TESTS") != "1" || string.IsNullOrWhiteSpace(configured))
            Assert.Inconclusive("未显式启用已批准的单库PG夹具，本项未运行。");
        NpgsqlConnectionStringBuilder settings;
        try { settings = new NpgsqlConnectionStringBuilder(configured); }
        catch (ArgumentException) { throw new InvalidOperationException("单库测试连接格式无效，不输出凭据。"); }
        Assert.AreEqual("192.168.3.12", settings.Host);
        Assert.AreEqual(5432, settings.Port);
        Assert.AreEqual(DatabaseName, settings.Database);
        Assert.IsTrue(string.IsNullOrEmpty(settings.SearchPath) || settings.SearchPath == "public");
        settings.SearchPath = "public";
        settings.Pooling = false;
        ConnectionString = settings.ConnectionString;
        var baselinePath = Path.Combine(root, "SwiftExport.Infrastructure/Database/Generated/SwiftExport.PostgreSql.Schema.preview.sql");
        baseline = File.ReadAllText(baselinePath);
        Assert.AreEqual(BaselineHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(baselinePath))));
        var begin = baseline.IndexOf("\nBEGIN;", StringComparison.Ordinal) + 1;
        var commit = baseline.LastIndexOf("COMMIT;", StringComparison.Ordinal);
        Assert.IsTrue(begin > 0 && commit > begin && baseline[(commit + 7)..].Trim().Length == 0);
        // 固定基线自带BEGIN/COMMIT；重置由此处事务拥有，去掉仅外层两句，避免脚本提前提交Npgsql事务。
        resetBaseline = baseline.Remove(commit, 7).Remove(begin, 6);
        exportDirectory = Path.Combine(root, "TestResults/postgresql-migration/p09-legacy-single-database");
        Directory.CreateDirectory(exportDirectory);
        // 文件租约同时隔离不同测试进程；碰到其他持有者直接停止，不把超时当作对方已退出。
        lease = new FileStream(Path.Combine(exportDirectory, "serial.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>首次必须确认同名库不存在；后续只凭本地创建事实及数据库标记接续本任务自己创建的库。</summary>
    public static async Task<PostgreSqlLegacyTestDatabase> AcquireAsync(TestContext context)
    {
        Assert.IsFalse(resetFailed, "本进程此前导出或重置失败，停止后续场景，不自动重试。");
        var database = new PostgreSqlLegacyTestDatabase(context);
        try { await database.InitializeAsync(); return database; }
        catch { await database.lease.DisposeAsync(); throw; }
    }

    private async Task InitializeAsync()
    {
        var markerPath = Path.Combine(exportDirectory, "database-owner.json");
        var settings = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres" };
        await using var administrator = new NpgsqlConnection(settings.ConnectionString);
        await administrator.OpenAsync();
        var existing = await administrator.QuerySingleOrDefaultAsync<DatabaseOwner>("""
            SELECT oid::bigint AS "Oid",shobj_description(oid,'pg_database') AS "Token"
            FROM pg_database WHERE datname=@Name
            """, new { Name = DatabaseName });
        if (!File.Exists(markerPath))
        {
            Assert.IsNull(existing, "同名库已存在且没有本任务创建事实，拒绝接管或重置。");
            var token = "SwiftExport-P09-20261006-" + Guid.NewGuid().ToString("N");
            await administrator.ExecuteAsync("CREATE DATABASE \"dv_next_pg_legacy_tests_20261006\"", commandTimeout: 90);
            await administrator.ExecuteAsync($"COMMENT ON DATABASE \"dv_next_pg_legacy_tests_20261006\" IS '{token}'");
            var oid = await administrator.ExecuteScalarAsync<long>("SELECT oid::bigint FROM pg_database WHERE datname=@Name", new { Name = DatabaseName });
            await File.WriteAllTextAsync(markerPath, JsonSerializer.Serialize(new DatabaseOwner { Oid = oid, Token = token }));
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync(baseline, commandTimeout: 90);
        }
        else
        {
            var expected = JsonSerializer.Deserialize<DatabaseOwner>(await File.ReadAllTextAsync(markerPath));
            Assert.IsNotNull(existing, "本任务新库已不存在，拒绝自动重新建库。");
            Assert.IsNotNull(expected);
            Assert.AreEqual(expected.Oid, existing.Oid);
            Assert.AreEqual(expected.Token, existing.Token, "新库所有权标记不一致，拒绝接管。");
        }
        await using var guard = new NpgsqlConnection(ConnectionString);
        await guard.OpenAsync();
        await RequireNoOtherClientsAsync(guard);
        var tables = await TablesAsync(guard);
        Assert.HasCount(50, tables);
        using var rows = await guard.QueryMultipleAsync(string.Join("\n", tables.Select(table => $"SELECT count(*) FROM public.\"{table}\";")));
        foreach (var table in tables)
            Assert.AreEqual(0L, await rows.ReadSingleAsync<long>(), $"上例{table}仍有事实；不在初始化时清理或掩盖失败。");
        initialized = true;
    }

    /// <summary>每例所有业务连接结束后导出50表及结构事实、核对文件SHA，再事务性重置本新库；失败则保留库现场。</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!initialized) return;
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await RequireNoOtherClientsAsync(connection);
            var tables = await TablesAsync(connection);
            Assert.HasCount(50, tables);
            var facts = new Dictionary<string, JsonElement>();
            using (var rows = await connection.QueryMultipleAsync(string.Join("\n", tables.Select(table =>
                $"SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY uid),'[]'::jsonb)::text FROM public.\"{table}\" t;"))))
                foreach (var table in tables)
                    facts.Add(table, JsonSerializer.Deserialize<JsonElement>(await rows.ReadSingleAsync<string>()));
            var structures = await connection.ExecuteScalarAsync<string>("""
                SELECT jsonb_build_object(
                  'columns',(SELECT jsonb_agg(to_jsonb(x) ORDER BY table_name,ordinal_position) FROM information_schema.columns x WHERE table_schema='public'),
                  'constraints',(SELECT jsonb_agg(jsonb_build_object('table',c.relname,'name',x.conname,'definition',pg_get_constraintdef(x.oid)) ORDER BY c.relname,x.conname)
                    FROM pg_constraint x JOIN pg_class c ON c.oid=x.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public'),
                  'indexes',(SELECT jsonb_agg(to_jsonb(x) ORDER BY tablename,indexname) FROM pg_indexes x WHERE schemaname='public'),
                  'triggers',(SELECT jsonb_agg(jsonb_build_object('table',c.relname,'definition',pg_get_triggerdef(t.oid)) ORDER BY c.relname,t.tgname)
                    FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND NOT t.tgisinternal),
                  'functions',(SELECT jsonb_agg(pg_get_functiondef(p.oid) ORDER BY p.oid) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND p.prokind='f'),
                  'sequences',(SELECT jsonb_agg(to_jsonb(x) ORDER BY sequencename) FROM pg_sequences x WHERE schemaname='public'))::text
                """);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Database = DatabaseName, context.FullyQualifiedTestClassName, context.TestName,
                OutcomeAtExport = context.CurrentTestOutcome.ToString(),
                ResultEvidence = "最终结果见本批实际测试日志及TRX；方法内Dispose时框架结果可能尚未定案",
                ExportedAtUtc = DateTime.UtcNow, BaselineHash, Tables = facts,
                Structures = JsonSerializer.Deserialize<JsonElement>(structures!)
            });
            var path = Path.Combine(exportDirectory, DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N") + ".json");
            await File.WriteAllBytesAsync(path, bytes);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            await File.WriteAllTextAsync(path + ".sha256", hash);
            Assert.AreEqual(hash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))));
            await RequireNoOtherClientsAsync(connection);
            await using var transaction = await connection.BeginTransactionAsync();
            await connection.ExecuteAsync("DROP SCHEMA public CASCADE; CREATE SCHEMA public;", transaction: transaction, commandTimeout: 90);
            await connection.ExecuteAsync(resetBaseline, transaction: transaction, commandTimeout: 90);
            await transaction.CommitAsync();
            Console.WriteLine($"PG单库事实已导出：{Path.GetRelativePath(root, path)}；SHA256={hash}；仅该新库public已重置。");
        }
        catch { resetFailed = true; throw; }
        finally { await lease.DisposeAsync(); }
    }

    /// <summary>只读取已批准新库public表名，按稳定顺序导出。</summary>
    private static async Task<string[]> TablesAsync(NpgsqlConnection connection)
        => (await connection.QueryAsync<string>("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename")).ToArray();

    /// <summary>等待已关闭的无池连接退出服务端；限定两秒，仍有任何其他客户端时拒绝重置，不终止连接。</summary>
    private static async Task RequireNoOtherClientsAsync(NpgsqlConnection connection)
    {
        var clients = 0;
        // DisposeAsync关闭客户端后，服务端进程可能稍后退出；只等待退出事实，不放行空闲连接或事务。
        for (var attempt = 0; attempt <= 20; attempt++)
        {
            clients = await connection.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid()");
            if (clients == 0) return;
            if (attempt < 20) await Task.Delay(100);
        }
        Assert.AreEqual(0, clients, "新库仍有其他连接，拒绝重置。");
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        throw new InvalidOperationException("无法定位单库测试导出目录。");
    }

    /// <summary>记录本任务新库OID和随机标记；不保存账号或连接凭据。</summary>
    private sealed class DatabaseOwner
    {
        public long Oid { get; set; }
        public string Token { get; set; } = string.Empty;
    }
}
