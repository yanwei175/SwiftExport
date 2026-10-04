using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Products.ProductMasters;
using SwiftExport.Infrastructure.Repositories.Products.ProductNeutrals;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>唯一性验收的本地辅助：结构核验、随机夹具、保存点和回滚，不修改共享夹具。</summary>
public sealed partial class PostgreSqlProductUniquenessIntegrationTests
{
    private const string CombinationIndex = "ux_product_neutral_master_color";

    /// <summary>每例固定专用保留库，先验已部署结构，所有数据回滚后独立连接检查随机审计夹具零留存。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, Task> scenario)
    {
        var connectionString = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var session = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        await AssertIndexes(session.Connection);
        var audit = Prefix();
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try { await scenario(Context(session, audit)); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        await AssertNoFixtures(observer.Connection, audit);
    }

    /// <summary>生成真实随机父及颜色，使中性编码碰撞不被其他唯一键或逻辑引用问题冒充。</summary>
    private static async Task<BaseEntity> NewEntity(RepositoryBaseContext context, bool master, string code)
    {
        if (master) return new ProductMaster
        { Uid = Guid.NewGuid(), MasterSku = code, DefaultDescriptionZh = "原描述", DefaultFeaturesEn = "原配置" };
        var parent = new ProductMaster { Uid = Guid.NewGuid(), MasterSku = Prefix() };
        var color = new ProductColor { Uid = Guid.NewGuid(), ColorCodeUnique = Prefix(), ColorCode = Prefix() };
        await Insert(context, parent);
        await Insert(context, color);
        return new ProductNeutral
        {
            Uid = Guid.NewGuid(), ProductCode = code, ProductMasterId = parent.Uid, ColorId = color.Uid,
            DefaultSalesPriceUsd = 12.5m, DefaultExchangeRate = 7.1m, ProfitMargin = 0.2m,
            DefaultPurchasePriceCny = 70m, ImagePath = "原图片", IsMainImage = false
        };
    }

    /// <summary>仅三种固定实体通过公共元数据插入原值，不修剪编码或使用仓储冲突转换。</summary>
    private static async Task Insert(RepositoryBaseContext context, BaseEntity entity, CancellationToken token = default)
    {
        Assert.IsTrue(entity is ProductMaster or ProductNeutral or ProductColor);
        context.AuditStamp.StampForInsert(entity);
        var metadata = PostgreSqlEntityMetadata.GetMetadata(entity.GetType());
        var columns = string.Join(",", metadata.Columns.Select(x => PostgreSqlNames.Quote(x.ColumnName)));
        var values = string.Join(",", metadata.Columns.Select(x => "@" + x.ParameterName));
        Assert.AreEqual(1, await context.Session.Connection.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO {PostgreSqlNames.Quote(metadata.SchemaName)}.{PostgreSqlNames.Quote(metadata.TableName)} ({columns}) VALUES ({values})",
            PostgreSqlParameters.Entity(entity, metadata), context.Session.Transaction, commandTimeout: 15, cancellationToken: token)));
    }

    /// <summary>真实仓储 Add/Update 路径按实体职责选择，不模拟占用错误或扩展共享仓储。</summary>
    private static Task<RepositoryWriteResult> Write(RepositoryBaseContext context, string repository, BaseEntity entity, bool update) => repository switch
    {
        "master" => update ? new PostgreSqlProductMasterRepository(context).UpdateAsync((ProductMaster)entity)
            : new PostgreSqlProductMasterRepository(context).AddAsync((ProductMaster)entity),
        "neutral" => update ? new PostgreSqlProductNeutralRepository(context).UpdateAsync((ProductNeutral)entity)
            : new PostgreSqlProductNeutralRepository(context).AddAsync((ProductNeutral)entity),
        "manager" => update ? new PostgreSqlProductNeutralManagerRepository(context).UpdateAsync((ProductNeutral)entity)
            : new PostgreSqlProductNeutralManagerRepository(context).AddAsync((ProductNeutral)entity),
        _ => throw new ArgumentOutOfRangeException(nameof(repository))
    };

    /// <summary>预期唯一错误只比较 SQLSTATE 和准确约束名，随后先恢复保存点，避免对失败事务继续查询。</summary>
    private static async Task ExpectCollision(RepositoryBaseContext context, string index, Func<Task> operation)
    {
        await Savepoint(context);
        var error = await Assert.ThrowsAsync<PostgresException>(operation);
        Assert.AreEqual("23505", error.SqlState);
        Assert.AreEqual(index, error.ConstraintName);
        await Restore(context);
    }

    /// <summary>固定保存点供每次预期失败恢复；名称仅为本类常量。</summary>
    private static Task Savepoint(RepositoryBaseContext context) => context.Session.Connection.ExecuteAsync(
        "SAVEPOINT product_collision", transaction: context.Session.Transaction);

    /// <summary>恢复并释放保存点后才能执行下一次数据库操作。</summary>
    private static async Task Restore(RepositoryBaseContext context)
    {
        await context.Session.Connection.ExecuteAsync("ROLLBACK TO SAVEPOINT product_collision", transaction: context.Session.Transaction);
        await context.Session.Connection.ExecuteAsync("RELEASE SAVEPOINT product_collision", transaction: context.Session.Transaction);
    }

    /// <summary>整行 JSON 比较覆盖 Version、编码、引用、价格、主图和全部审计字段，不局限于错误文本。</summary>
    private static Task<string> Snapshot(RepositoryBaseContext context, bool master, Guid uid) => context.Session.Connection.QuerySingleAsync<string>(
        $"SELECT to_jsonb(t)::text FROM public.{Table(master)} t WHERE uid=@Uid", new { Uid = uid }, context.Session.Transaction);

    /// <summary>读取两个表达式编码索引和原组合索引，不部署 DDL 或接受缺失索引作为跳过。</summary>
    private static async Task AssertIndexes(IDbConnection connection)
    {
        foreach (var master in new[] { true, false })
        {
            var shape = await ReadIndex(connection, Table(master), CodeIndex(master));
            Assert.IsNotNull(shape, $"{CodeIndex(master)} 未部署。");
            Assert.IsTrue(shape.Unique && shape.Valid && shape.Ready && shape.Expression && shape.Collation);
            Assert.AreEqual(1, shape.Keys);
            Assert.AreEqual(1, shape.Attributes);
            var expression = Regex.Replace(shape.Key1, @"\s+COLLATE\s+(?:public\.)?erp_dictionary_ci(?=\)*$)", "", RegexOptions.CultureInvariant);
            Assert.IsTrue(Regex.IsMatch(expression,
                $@"^\(*rtrim\(\s*\(*{Column(master)}\)*\s*(?:::text)?\s*,\s*' '\s*(?:::text)?\s*\)\)*$", RegexOptions.CultureInvariant));
            Assert.AreEqual("is_deleted=false", Regex.Replace(shape.Predicate, @"[\s()]", ""));
        }
        var combined = await ReadIndex(connection, "product_neutral", CombinationIndex);
        Assert.IsNotNull(combined);
        Assert.IsTrue(combined.Unique && combined.Valid && combined.Ready && !combined.Expression);
        Assert.AreEqual(2, combined.Keys);
        Assert.AreEqual(2, combined.Attributes);
        Assert.AreEqual("product_master_id", combined.Key1);
        Assert.AreEqual("color_id", combined.Key2);
        Assert.AreEqual("is_deleted=false", Regex.Replace(combined.Predicate, @"[\s()]", ""));
    }

    /// <summary>按真实表和索引读取目录状态，编码规则必须引用 public 下指定 ICU 对象。</summary>
    private static Task<IndexShape?> ReadIndex(IDbConnection connection, string table, string index) => connection.QuerySingleOrDefaultAsync<IndexShape>("""
        SELECT i.indisunique AS "Unique",i.indisvalid AS "Valid",i.indisready AS "Ready",
               i.indnkeyatts AS "Keys",i.indnatts AS "Attributes",(i.indexprs IS NOT NULL) AS "Expression",
               pg_get_indexdef(i.indexrelid,1,true) AS "Key1",
               CASE WHEN i.indnkeyatts>1 THEN pg_get_indexdef(i.indexrelid,2,true) ELSE '' END AS "Key2",
               pg_get_expr(i.indpred,i.indrelid) AS "Predicate",
               i.indcollation[0]=to_regcollation('public.erp_dictionary_ci') AS "Collation"
        FROM pg_catalog.pg_index i
        WHERE i.indrelid=to_regclass('public.' || @Table) AND i.indexrelid=to_regclass('public.' || @Index)
        """, new { Table = table, Index = index });

    /// <summary>回滚后仅按本次随机审计名检查夹具，不以业务前缀清理任何数据。</summary>
    private static async Task AssertNoFixtures(IDbConnection connection, string audit)
    {
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("""
            SELECT (SELECT count(*) FROM public.product_master WHERE created_by=@Audit)
                 + (SELECT count(*) FROM public.product_neutral WHERE created_by=@Audit)
                 + (SELECT count(*) FROM public.product_color WHERE created_by=@Audit)
            """, new { Audit = audit }));
    }

    /// <summary>构造共享会话和本次随机审计身份，不创建隐式连接。</summary>
    private static RepositoryBaseContext Context(DbSession session, string audit)
    {
        var user = new TestUser(audit);
        return new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
    }

    /// <summary>固定实体到物理表，不接受外部标识符。</summary>
    private static string Table(bool master) => master ? "product_master" : "product_neutral";
    /// <summary>固定编码列，不改变列定义。</summary>
    private static string Column(bool master) => master ? "master_sku" : "product_code";
    /// <summary>准确编码索引名用于排除其他约束误报。</summary>
    private static string CodeIndex(bool master) => "ux_" + Table(master) + "_" + Column(master);
    /// <summary>随机短编码与审计名隔离每例夹具。</summary>
    private static string Prefix() => "PGPU" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>目录核验只读投影，不用于业务传输。</summary>
    private sealed class IndexShape
    {
        public bool Unique { get; set; }
        public bool Valid { get; set; }
        public bool Ready { get; set; }
        public bool Expression { get; set; }
        public bool Collation { get; set; }
        public int Keys { get; set; }
        public int Attributes { get; set; }
        public string Key1 { get; set; } = "";
        public string Key2 { get; set; } = "";
        public string Predicate { get; set; } = "";
    }

    /// <summary>仅提供本例审计名，不模拟已有业务用户。</summary>
    private sealed class TestUser(string audit) : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => audit;
        public bool IsAuthenticated => false;
    }
}
