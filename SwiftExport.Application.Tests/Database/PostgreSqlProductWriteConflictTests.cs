using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Products.ProductMasters;
using SwiftExport.Infrastructure.Repositories.Products.ProductNeutrals;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>通过真实仓储 override 及 Dapper 命令验证精确唯一冲突转换；自含执行故障替身，不改共享夹具。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>三个仓储的新增更新均命中精确白名单，旧版本在基类递增前保存，失败没有新版本或影响行。</summary>
    [TestMethod]
    [DataRow("master", "ux_product_master_master_sku", "PRODUCT_MASTER_SKU_OCCUPIED", "主型号编码已被占用", 3)]
    [DataRow("master", "ux_product_master_master_sku", "PRODUCT_MASTER_SKU_OCCUPIED", "主型号编码已被占用", 0)]
    [DataRow("neutral", "ux_product_neutral_product_code", "PRODUCT_NEUTRAL_CODE_OCCUPIED", "中性产品编码已被占用", 3)]
    [DataRow("neutral", "ux_product_neutral_product_code", "PRODUCT_NEUTRAL_CODE_OCCUPIED", "中性产品编码已被占用", 0)]
    [DataRow("manager", "ux_product_neutral_product_code", "PRODUCT_NEUTRAL_CODE_OCCUPIED", "中性产品编码已被占用", 3)]
    [DataRow("manager", "ux_product_neutral_product_code", "PRODUCT_NEUTRAL_CODE_OCCUPIED", "中性产品编码已被占用", 0)]
    public async Task ProductKnownUniqueConflict_ReturnsFixedResult(string kind, string index, string code, string message, int version)
    {
        foreach (var update in new[] { false, true })
        {
            using var fixture = new ProductConflictFixture(Failure(kind, index));
            var entity = ConflictEntity(kind, version);
            var uid = entity.Uid;
            var result = await ConflictWrite(fixture.Context, kind, entity, update);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(entity.Uid, result.Uid);
            if (update) Assert.AreEqual(uid, result.Uid);
            Assert.AreEqual(update ? (int?)(version <= 0 ? 1 : version) : null, result.OldVersion);
            Assert.IsNull(result.NewVersion);
            Assert.AreEqual(0, result.AffectedRows);
            Assert.AreEqual(code, result.ErrorCode);
            Assert.AreEqual(message, result.Message);
            Assert.AreEqual(update ? (version <= 0 ? 2 : version + 1) : (version <= 0 ? 1 : version), entity.Version);
            Assert.AreEqual(1, fixture.Connection.Commands);
            Assert.AreEqual(0, fixture.Connection.Begins);
            if (update) Assert.AreEqual(version <= 0 ? 1 : version, fixture.Connection.Values["OriginalVersion"]);
            StringAssert.Contains(fixture.Connection.Sql, update ? "UPDATE" : "INSERT INTO");
        }
    }

    /// <summary>主键未知空名、错误作用域、其他 SQLSTATE、超时及取消均抛出同一个异常，不被转成业务唯一冲突。</summary>
    [TestMethod]
    [DataRow("master")]
    [DataRow("neutral")]
    [DataRow("manager")]
    public async Task ProductOtherFailures_PropagateOriginalException(string kind)
    {
        var known = kind == "master" ? "ux_product_master_master_sku" : "ux_product_neutral_product_code";
        var table = kind == "master" ? "product_master" : "product_neutral";
        var failures = new Exception[]
        {
            Failure(kind, "pk_" + table), Failure(kind, "unknown_index"), Failure(kind, null), Failure(kind, ""),
            Failure(kind, known, schema: "other"), Failure(kind, known, schema: "PUBLIC"), Failure(kind, known, schema: null),
            Failure(kind, known, table: "other"), Failure(kind, known, table: null),
            Failure(kind, kind == "master" ? "ux_product_neutral_product_code" : "ux_product_master_master_sku"),
            Failure(kind, known, state: "40P01"), Failure(kind, known, state: "40001"), Failure(kind, known, state: "57014"),
            Failure(kind, known, state: "23514"), new TimeoutException("测试超时"),
            new NpgsqlException("驱动测试超时", new TimeoutException("内部测试超时")), new OperationCanceledException("测试取消")
        };
        foreach (var failure in failures)
        foreach (var update in new[] { false, true })
        {
            using var fixture = new ProductConflictFixture(failure);
            var observed = await Assert.ThrowsAsync<Exception>(() => ConflictWrite(fixture.Context, kind, ConflictEntity(kind, 3), update));
            Assert.AreSame(failure, observed);
            Assert.AreEqual(1, fixture.Connection.Commands);
            Assert.AreEqual(0, fixture.Connection.Begins);
        }
    }

    /// <summary>无异常时新增更新仍执行基类并返回成功，不主动创建事务或替换普通写入结果。</summary>
    [TestMethod]
    [DataRow("master")]
    [DataRow("neutral")]
    [DataRow("manager")]
    public async Task ProductOverrides_DelegateSuccessfulWrites(string kind)
    {
        foreach (var update in new[] { false, true })
        {
            using var fixture = new ProductConflictFixture(null);
            var entity = ConflictEntity(kind, 3);
            var result = await ConflictWrite(fixture.Context, kind, entity, update);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(entity.Uid, result.Uid);
            Assert.AreEqual(update ? 4 : 3, result.NewVersion);
            Assert.AreEqual(1, result.AffectedRows);
            Assert.AreEqual(1, fixture.Connection.Commands);
            Assert.AreEqual(0, fixture.Connection.Begins);
        }
    }

    /// <summary>按现有仓储入口真实调用 override，不直接调用内部白名单函数。</summary>
    private static Task<RepositoryWriteResult> ConflictWrite(RepositoryBaseContext context, string kind, BaseEntity entity, bool update)
    {
        if (kind == "master")
        {
            var repository = new PostgreSqlProductMasterRepository(context);
            return update ? repository.UpdateAsync((ProductMaster)entity) : repository.AddAsync((ProductMaster)entity);
        }
        PostgreSqlDapperRepository<ProductNeutral> neutral = kind switch
        {
            "neutral" => new PostgreSqlProductNeutralRepository(context),
            "manager" => new PostgreSqlProductNeutralManagerRepository(context),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return update ? neutral.UpdateAsync((ProductNeutral)entity) : neutral.AddAsync((ProductNeutral)entity);
    }

    /// <summary>构造最小有效实体及指定版本，以观察基类真实审计、身份和版本副作用。</summary>
    private static BaseEntity ConflictEntity(string kind, int version) => kind == "master"
        ? new ProductMaster { MasterSku = "M01", Version = version }
        : new ProductNeutral { ProductCode = "N01", ProductMasterId = Guid.NewGuid(), ColorId = Guid.NewGuid(), Version = version };

    /// <summary>构造驱动实际异常类型，使用敏感占位消息确认回执只保留固定业务文案。</summary>
    private static PostgresException Failure(string kind, string? index, string state = "23505", string? schema = "public", string? table = "expected")
        => new("不可对外输出的数据库详情", "ERROR", "ERROR", state, schemaName: schema,
            tableName: table == "expected" ? (kind == "master" ? "product_master" : "product_neutral") : table, constraintName: index);

    /// <summary>只为本组读写故障提供会话，复用项目既有测试用户和标准审计上下文。</summary>
    private sealed class ProductConflictFixture : IDisposable
    {
        internal readonly ProductConflictConnection Connection;
        internal readonly DbSession Session;
        internal readonly RepositoryBaseContext Context;
        internal ProductConflictFixture(Exception? failure)
        {
            Connection = new ProductConflictConnection(failure);
            Session = new DbSession(new ProductConflictFactory(Connection));
            var user = new User();
            Context = new RepositoryBaseContext(Session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        }
        public void Dispose() => Session.Dispose();
    }

    /// <summary>向当前测试会话提供无网络连接，不修改共享 RecordingConnection。</summary>
    private sealed class ProductConflictFactory(ProductConflictConnection connection) : IDbConnectionFactory
    {
        public IDbConnection CreateConnection() => connection;
    }

    /// <summary>既有夹具没有读取故障回调，此最小替身同时覆盖 INSERT 非查询及 UPDATE RETURNING 读取故障。</summary>
    private sealed class ProductConflictConnection(Exception? failure) : DbConnection
    {
        internal int Commands;
        internal int Begins;
        internal string Sql = "";
        internal Dictionary<string, object?> Values = new();
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "in-memory-product-conflict";
        public override string DataSource => "none";
        public override string ServerVersion => "test";
        public override ConnectionState State => ConnectionState.Open;
        public override void Open() { }
        public override void Close() { }
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        /// <summary>任何新增事务都立即失败并计数，确保冲突转换没有偷偷拥有事务或重试边界。</summary>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        { Begins++; throw new NotSupportedException("本组测试不允许仓储创建事务。"); }
        protected override DbCommand CreateDbCommand() => new ProductConflictCommand(this, failure);

        /// <summary>通过真实 Npgsql 参数容器接收 Dapper 绑定，只在执行点注入原异常，禁止网络操作。</summary>
        private sealed class ProductConflictCommand(ProductConflictConnection connection, Exception? failure) : DbCommand
        {
            private readonly NpgsqlCommand parameters = new();
            [AllowNull] public override string CommandText { get; set; } = "";
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; } = connection;
            protected override DbTransaction? DbTransaction { get; set; }
            protected override DbParameterCollection DbParameterCollection => parameters.Parameters;
            protected override DbParameter CreateDbParameter() => new NpgsqlParameter();
            public override void Cancel() { }
            public override void Prepare() { }
            public override int ExecuteNonQuery() { Capture(); return 1; }
            public override object ExecuteScalar() { Capture(); return 4; }
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            {
                Capture();
                var rows = new DataTable();
                rows.Columns.Add("Version", typeof(int));
                rows.Rows.Add(4);
                return rows.CreateDataReader();
            }

            /// <summary>记录一次真正执行及绑定值，再抛原异常，便于断言没有重试且输入旧版本未被改写。</summary>
            private void Capture()
            {
                connection.Commands++;
                connection.Sql = CommandText;
                connection.Values = parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => (object?)p.Value);
                if (failure is not null) throw failure;
            }
            protected override void Dispose(bool disposing)
            { if (disposing) parameters.Dispose(); base.Dispose(disposing); }
        }
    }
}
