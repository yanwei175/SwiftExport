using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>索引部署后的分类颜色唯一性验收；只新增随机记录并回滚，不部署结构或提交数据。</summary>
[TestClass]
public sealed class PostgreSqlClassificationUniquenessIntegrationTests
{
    /// <summary>有效分类可同时保存多个 NULL 编码，不能为了唯一索引测试补造编码或改变原有空值。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationUniquenessIntegration")]
    public Task CategoryNullCodes_DoNotParticipateInUniqueKey() => InRollback(async context =>
    {
        var ids = new[] { await Insert(context, "product_category", null), await Insert(context, "product_category", null) };
        Assert.AreEqual(2, await context.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM public.product_category WHERE uid=ANY(@Uids) AND category_code IS NULL AND is_deleted=FALSE",
            PostgreSqlParameters.Ids(ids), context.Session.Transaction));
    });

    /// <summary>非 NULL 空串与 ASCII 空格码占同一唯一键；已有有效空码时只读判别后未运行，不清理或覆盖旧记录。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationUniquenessIntegration")]
    [DataRow("product_category")]
    [DataRow("product_color")]
    public Task EmptyAndAsciiSpaceCodes_ConflictWithoutTouchingExistingBlankRows(string table) => InRollback(async context =>
    {
        var column = CodeColumn(table);
        var exists = await context.Session.Connection.ExecuteScalarAsync<bool>($"""
            SELECT EXISTS (SELECT 1 FROM public.{table}
                WHERE is_deleted=FALSE AND {column} IS NOT NULL
                  AND rtrim({column},' ') COLLATE public.erp_dictionary_ci = '' COLLATE public.erp_dictionary_ci)
            """, transaction: context.Session.Transaction);
        if (exists) Assert.Inconclusive("已有有效空码占用唯一键；本例不写入、不清理旧行，未运行空码夹具。");
        var uid = await Insert(context, table, "");
        await context.Session.Connection.ExecuteAsync("SAVEPOINT collision", transaction: context.Session.Transaction);
        var error = await Assert.ThrowsAsync<PostgresException>(() => Insert(context, table, "   "));
        AssertCollision(error, table);
        await context.Session.Connection.ExecuteAsync("ROLLBACK TO SAVEPOINT collision", transaction: context.Session.Transaction);
        Assert.AreEqual("", await context.Session.Connection.ExecuteScalarAsync<string>(
            $"SELECT {column} FROM public.{table} WHERE uid=@Uid", new { Uid = uid }, context.Session.Transaction));
    });

    /// <summary>颜色普通业务编码允许重复，唯一键只属于 ColorCodeUnique，不额外扩展普通 ColorCode 约束。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationUniquenessIntegration")]
    public Task ColorOrdinaryCodes_CanRepeatWithDistinctUniqueCodes() => InRollback(async context =>
    {
        var prefix = Prefix();
        var ids = new[]
        {
            await Insert(context, "product_color", prefix + "A", ordinaryColorCode: prefix),
            await Insert(context, "product_color", prefix + "B", ordinaryColorCode: prefix)
        };
        var parameters = PostgreSqlParameters.Ids(ids);
        parameters.Add("Code", prefix);
        Assert.AreEqual(2, await context.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM public.product_color WHERE uid=ANY(@Uids) AND color_code=@Code", parameters, context.Session.Transaction));
    });

    /// <summary>大小写及 ASCII 尾空格必须命中指定活动编码唯一索引；错误事务通过保存点恢复。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationUniquenessIntegration")]
    [DataRow("product_category", false)]
    [DataRow("product_category", true)]
    [DataRow("product_color", false)]
    [DataRow("product_color", true)]
    public Task EquivalentCodes_RejectWithExactIndex(string table, bool trailingSpace) => InRollback(async context =>
    {
        var code = Prefix() + "Ab";
        await Insert(context, table, code);
        await context.Session.Connection.ExecuteAsync("SAVEPOINT collision", transaction: context.Session.Transaction);
        var error = await Assert.ThrowsAsync<PostgresException>(() => Insert(context, table,
            trailingSpace ? code + " " : code.ToLowerInvariant()));
        AssertCollision(error, table);
        await context.Session.Connection.ExecuteAsync("ROLLBACK TO SAVEPOINT collision", transaction: context.Session.Transaction);
    });

    /// <summary>前导空格和重音保持区别，软删记录不占有效编码唯一键；分类与颜色都没有启停字段。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationUniquenessIntegration")]
    [DataRow("product_category")]
    [DataRow("product_color")]
    public Task DistinctAndDeletedCodes_PreservePartialIndexBoundary(string table) => InRollback(async context =>
    {
        var code = Prefix();
        var ids = new List<Guid>
        {
            await Insert(context, table, code + "e"),
            await Insert(context, table, " " + code + "e"),
            await Insert(context, table, code + "é"),
            await Insert(context, table, code + "Deleted", deleted: true),
            await Insert(context, table, code.ToLowerInvariant() + "deleted ")
        };
        Assert.AreEqual(ids.Count, await context.Session.Connection.ExecuteScalarAsync<int>(
            $"SELECT count(*) FROM public.{table} WHERE uid=ANY(@Uids)", PostgreSqlParameters.Ids(ids), context.Session.Transaction));
    });

    /// <summary>碰撞更新连同版本增加一起失败；保存点回滚后数据库原码和版本保持原值。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationUniquenessIntegration")]
    [DataRow("product_category")]
    [DataRow("product_color")]
    public Task CollidingUpdate_RollbackPreservesTextAndVersion(string table) => InRollback(async context =>
    {
        var prefix = Prefix();
        await Insert(context, table, prefix + "Taken");
        var original = prefix + "Original ";
        var uid = await Insert(context, table, original);
        var column = CodeColumn(table);
        await context.Session.Connection.ExecuteAsync("SAVEPOINT collision", transaction: context.Session.Transaction);
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Session.Connection.ExecuteAsync(
            $"UPDATE public.{table} SET {column}=@Code,version=version+1 WHERE uid=@Uid AND version=1",
            new { Code = prefix.ToLowerInvariant() + "taken ", Uid = uid }, context.Session.Transaction));
        AssertCollision(error, table);
        await context.Session.Connection.ExecuteAsync("ROLLBACK TO SAVEPOINT collision", transaction: context.Session.Transaction);
        var saved = await context.Session.Connection.QuerySingleAsync<StoredCode>(
            $"SELECT {column} AS Code,version AS Version FROM public.{table} WHERE uid=@Uid", new { Uid = uid }, context.Session.Transaction);
        Assert.AreEqual(original, saved.Code);
        Assert.AreEqual(1, saved.Version);
    });

    /// <summary>等价编码插入必须等待未提交竞争者；观测实际事务锁后回滚 A，使 B 成功，再回滚 B。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationUniquenessIntegration")]
    [DataRow("product_category")]
    [DataRow("product_color")]
    public async Task ConcurrentInsert_WaitsThenSucceedsAfterRollback(string table)
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var first = new DbSession(new PostgreSqlConnectionFactory(connection));
        using var second = new DbSession(new PostgreSqlConnectionFactory(connection));
        await AssertExpectedIndexes(first.Connection);
        using var transactionA = first.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        using var transactionB = second.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        first.Transaction = transactionA;
        second.Transaction = transactionB;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Task<Guid>? pending = null;
        var rolledBackA = false;
        try
        {
            var pidA = await first.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: transactionA);
            var pidB = await second.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: transactionB);
            var code = Prefix() + "Race";
            await Insert(Context(first), table, code);
            pending = Insert(Context(second), table, code.ToLowerInvariant() + " ", token: deadline.Token);
            await ObserveWait(first, pidA, pidB, pending, deadline.Token);
            Assert.IsFalse(pending.IsCompleted, "锁观测完成时竞争插入不应已完成。");
            transactionA.Rollback();
            rolledBackA = true;
            first.Transaction = null;
            var uid = await pending;
            var column = CodeColumn(table);
            Assert.AreEqual(code.ToLowerInvariant() + " ", await second.Connection.ExecuteScalarAsync<string>(
                $"SELECT {column} FROM public.{table} WHERE uid=@Uid", new { Uid = uid }, transactionB));
        }
        finally
        {
            // 先解除 A 的锁并取消/收尾 B 的在途命令，再回滚 B；失败路径也不留下正在使用事务的命令。
            try
            {
                if (!rolledBackA) transactionA.Rollback();
            }
            finally
            {
                first.Transaction = null;
                deadline.Cancel();
                try { if (pending is not null) await pending; }
                catch (Exception) { /* 主路径保留失败断言；此处只收尾已观察的任务异常。 */ }
                try { transactionB.Rollback(); }
                finally { second.Transaction = null; }
            }
        }
    }

    /// <summary>刷新统计快照并确认 B 等待事务锁，阻塞来源确实是 A；轮询延时仅用于让出线程。</summary>
    private static async Task ObserveWait(DbSession session, int pidA, int pidB, Task pending, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await session.Connection.ExecuteAsync(new CommandDefinition("SELECT pg_stat_clear_snapshot()", transaction: session.Transaction, cancellationToken: token));
            var waiting = await session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS (
                    SELECT 1 FROM pg_stat_activity a
                    WHERE a.pid=@PidB AND a.state='active' AND a.wait_event_type='Lock'
                      AND @PidA=ANY(pg_blocking_pids(a.pid))
                      AND EXISTS (SELECT 1 FROM pg_locks l WHERE l.pid=a.pid AND NOT l.granted AND l.locktype='transactionid'))
                """, new { PidA = pidA, PidB = pidB }, session.Transaction, cancellationToken: token));
            if (waiting) return;
            Assert.IsFalse(pending.IsCompleted, "未观测到预期竞争锁，插入已提前结束。");
            await Task.Delay(50, token);
        }
        Assert.Fail("未在期限内观测到指定竞争连接的事务锁等待。");
    }

    /// <summary>每例首先检查两个已部署索引；缺失、普通列索引或不符的软删谓词均明确失败，不能以业务样例代替结构验收。</summary>
    private static async Task AssertExpectedIndexes(IDbConnection connection)
    {
        foreach (var table in new[] { "product_category", "product_color" })
        {
            var index = await connection.QuerySingleOrDefaultAsync<IndexShape>("""
                SELECT i.indisunique AS IsUnique,i.indisvalid AS IsValid,i.indisready AS IsReady,
                    i.indnkeyatts AS KeyCount,i.indnatts AS AttributeCount,
                    pg_get_indexdef(i.indexrelid,1,true) AS Expression,
                    pg_get_expr(i.indpred,i.indrelid) AS Predicate,
                    (i.indexprs IS NOT NULL) AS HasExpression,
                    EXISTS (SELECT 1 FROM pg_collation col JOIN pg_namespace cn ON cn.oid=col.collnamespace
                        WHERE col.oid=i.indcollation[0] AND cn.nspname='public' AND col.collname='erp_dictionary_ci') AS ExpectedCollation
                FROM pg_index i JOIN pg_class idx ON idx.oid=i.indexrelid
                    JOIN pg_class t ON t.oid=i.indrelid JOIN pg_namespace n ON n.oid=t.relnamespace
                WHERE n.nspname='public' AND t.relname=@Table AND idx.relname=@Index
                """, new { Table = table, Index = IndexName(table) });
            Assert.IsNotNull(index, $"目标索引 {IndexName(table)} 尚未部署。");
            Assert.IsTrue(index.IsUnique && index.IsValid && index.IsReady && index.HasExpression && index.ExpectedCollation,
                $"{IndexName(table)} 必须为有效的指定 ICU 表达式唯一索引。");
            Assert.AreEqual(1, index.KeyCount);
            Assert.AreEqual(1, index.AttributeCount);
            // pg_get_indexdef 会输出 varchar 到 text 的隐式强转；仅容许这类展示差异，不忽略字符串字面量。
            var expression = Regex.Replace(index.Expression, @"\s+COLLATE\s+(?:public\.)?erp_dictionary_ci(?=\)*$)", "", RegexOptions.CultureInvariant);
            Assert.IsTrue(Regex.IsMatch(expression,
                $@"^\(*rtrim\(\s*\(*{CodeColumn(table)}\)*\s*(?:::text)?\s*,\s*' '\s*(?:::text)?\s*\)\)*$", RegexOptions.CultureInvariant),
                $"{IndexName(table)} 必须仅规范化 ASCII 尾空格。");
            var expectedPredicate = table == "product_category" ? "is_deleted=falseANDcategory_codeISNOTNULL" : "is_deleted=false";
            Assert.AreEqual(expectedPredicate, Regex.Replace(index.Predicate, @"[\s()]", ""),
                $"{IndexName(table)} 分类必须排除 NULL 码，颜色仅排除软删记录，不得附加其他条件。");
        }
    }

    /// <summary>通过既有专用环境开事务，每例都验证结构，finally 只回滚自己的随机数据。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, Task> scenario)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        await AssertExpectedIndexes(session.Connection);
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try { await scenario(Context(session)); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>仅允许两个固定主数据表，通过既有元数据参数保存随机记录，保留软删及分类空码测试边界。</summary>
    private static async Task<Guid> Insert(RepositoryBaseContext context, string table, string? code, bool deleted = false,
        CancellationToken token = default, string? ordinaryColorCode = null)
    {
        _ = CodeColumn(table);
        BaseEntity entity = table == "product_category"
            ? new ProductCategory { Uid = Guid.NewGuid(), CategoryName = Prefix(), CategoryCode = code, IsDeleted = deleted, Version = 1 }
            : new ProductColor { Uid = Guid.NewGuid(), ColorCodeUnique = code!, ColorCode = ordinaryColorCode ?? Prefix(), IsDeleted = deleted, Version = 1 };
        context.AuditStamp.StampForInsert(entity);
        var metadata = PostgreSqlEntityMetadata.GetMetadata(entity.GetType());
        var columns = string.Join(",", metadata.Columns.Select(c => PostgreSqlNames.Quote(c.ColumnName)));
        var values = string.Join(",", metadata.Columns.Select(c => "@" + c.ParameterName));
        Assert.AreEqual(1, await context.Session.Connection.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO public.{table} ({columns}) VALUES ({values})", PostgreSqlParameters.Entity(entity, metadata),
            context.Session.Transaction, commandTimeout: 15, cancellationToken: token)));
        return entity.Uid;
    }

    /// <summary>构造与仓储测试相同的审计上下文，不创建额外连接或业务框架。</summary>
    private static RepositoryBaseContext Context(DbSession session)
    {
        var user = new TestUser();
        return new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
    }

    /// <summary>校验数据库唯一冲突及确切索引名，防止别的约束碰撞冒充成功。</summary>
    private static void AssertCollision(PostgresException error, string table)
    {
        Assert.AreEqual("23505", error.SqlState);
        Assert.AreEqual(IndexName(table), error.ConstraintName);
    }

    /// <summary>固定表到编码列的白名单映射，调用方参数不能成为任意 SQL 标识符。</summary>
    private static string CodeColumn(string table) => table switch
    { "product_category" => "category_code", "product_color" => "color_code_unique", _ => throw new ArgumentOutOfRangeException(nameof(table)) };

    /// <summary>沿用现有生成器的物理索引命名，不批准或创建该索引。</summary>
    private static string IndexName(string table) => "ux_" + table + "_" + CodeColumn(table);

    /// <summary>每例随机短编码避免引用已有业务记录。</summary>
    private static string Prefix() => "PGU" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>保存数据库原文和版本的最小只读投影。</summary>
    private sealed class StoredCode
    {
        public string Code { get; set; } = "";
        public int Version { get; set; }
    }

    /// <summary>读取索引真实结构而非生成脚本，以阻止未部署或错误部署时通过。</summary>
    private sealed class IndexShape
    {
        public bool IsUnique { get; set; }
        public bool IsValid { get; set; }
        public bool IsReady { get; set; }
        public bool HasExpression { get; set; }
        public bool ExpectedCollation { get; set; }
        public int KeyCount { get; set; }
        public int AttributeCount { get; set; }
        public string Expression { get; set; } = "";
        public string Predicate { get; set; } = "";
    }

    /// <summary>仅为随机测试记录提供审计名称，不模拟用户权限。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_classification_unique__";
        public bool IsAuthenticated => false;
    }
}
