using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>通过真实 Dapper 调用与无网络 ADO.NET 替身验证列、参数、结果及事务传递。</summary>
[TestClass]
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>新增绑定实体 Quantity 参数，物理 INSERT 使用 QTY，且沿用调用方事务。</summary>
    [TestMethod]
    public async Task Insert_UsesMappedColumnAndPropertyParameter()
    {
        using var fixture = new Fixture();
        var entity = new PurchaseOrderLine { Quantity = 12.5m };
        var result = await fixture.Repository.AddAsync(entity);
        Assert.IsTrue(result.Success);
        StringAssert.Contains(fixture.Connection.Text, "INSERT INTO \"public\".\"purchase_order_line\"");
        StringAssert.Contains(fixture.Connection.Text, "\"quantity\"");
        Assert.IsFalse(fixture.Connection.Text.Contains("\"QTY\"", StringComparison.Ordinal));
        Assert.AreEqual(12.5m, fixture.Connection.Values["Quantity"]);
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>查询通过别名恢复 Quantity，不设置影响其他 DTO 的 Dapper 全局类型映射。</summary>
    [TestMethod]
    public async Task Read_UsesExplicitProjectionAndMapsResult()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("Quantity", typeof(decimal));
        fixture.Connection.Rows.Rows.Add(7.25m);
        var entity = await fixture.Repository.GetByIdAsync(Guid.NewGuid(), true);
        Assert.AreEqual(7.25m, entity!.Quantity);
        StringAssert.Contains(fixture.Connection.Text, "\"quantity\" AS \"Quantity\"");
        StringAssert.Contains(fixture.Connection.Text, "FOR UPDATE");
        Assert.IsFalse(fixture.Connection.Text.Contains("SELECT *", StringComparison.Ordinal));
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>更新保持原版本参数与数据库递增表达式；零行返回原并发错误。</summary>
    [TestMethod]
    public async Task Update_PreservesVersionContractAndConflict()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("Version", typeof(int));
        fixture.Connection.Rows.Rows.Add(4);
        var result = await fixture.Repository.UpdateAsync(new PurchaseOrderLine { Quantity = 2m, Version = 3 });
        Assert.IsTrue(result.Success);
        Assert.AreEqual(3, fixture.Connection.Values["OriginalVersion"]);
        StringAssert.Contains(fixture.Connection.Text, "\"quantity\" = @Quantity");
        StringAssert.Contains(fixture.Connection.Text, "\"version\" = \"version\" + 1");
        fixture.Connection.Rows.Rows.Clear();
        result = await fixture.Repository.UpdateAsync(new PurchaseOrderLine { Version = 3 });
        Assert.IsFalse(result.Success);
        Assert.AreEqual("CONCURRENCY_CONFLICT", result.ErrorCode);
    }

    /// <summary>软删除继续受版本与活动状态约束，硬删除仍传递既有事务。</summary>
    [TestMethod]
    public async Task Delete_PreservesExistingSoftAndHardBehavior()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("Version", typeof(int));
        fixture.Connection.Rows.Rows.Add(3);
        Assert.IsTrue((await fixture.Repository.DeleteAsync(Guid.NewGuid(), 3)).Success);
        StringAssert.Contains(fixture.Connection.Text, "\"is_deleted\" = TRUE");
        StringAssert.Contains(fixture.Connection.Text, "\"version\" = @Version");
        Assert.IsTrue((await fixture.Repository.DeleteAsync(Guid.NewGuid(), 3, false)).Success);
        StringAssert.Contains(fixture.Connection.Text, "DELETE FROM \"public\".\"purchase_order_line\"");
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>数组作为单个参数传递，读取保留软删除和空时间排序约定。</summary>
    [TestMethod]
    public async Task Lists_UseArrayAndPreserveFilters()
    {
        using var fixture = new Fixture();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await fixture.Repository.GetByIdListAsync(ids, true);
        CollectionAssert.AreEqual(ids, (Guid[])fixture.Connection.Values["Uids"]!);
        StringAssert.Contains(fixture.Connection.Text, "= ANY(@Uids)");
        StringAssert.Contains(fixture.Connection.Text, "= FALSE OR");
        await fixture.Repository.GetAllAsync();
        StringAssert.Contains(fixture.Connection.Text, "DESC NULLS LAST");
        await fixture.Repository.GetSingleEntityAsync();
        StringAssert.Contains(fixture.Connection.Text, "LIMIT 1");
        Assert.IsFalse(fixture.Connection.Text.Contains("WHERE", StringComparison.Ordinal));
    }

    /// <summary>无事务时拒绝锁查询，空集合不向数据库发命令。</summary>
    [TestMethod]
    public async Task Lock_RequiresTransaction_EmptyListDoesNotExecute()
    {
        using var fixture = new Fixture();
        fixture.Session.Transaction!.Dispose();
        fixture.Session.Transaction = null;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Repository.GetByIdAsync(Guid.NewGuid(), true));
        await fixture.Repository.GetByIdListAsync([]);
        Assert.AreEqual("", fixture.Connection.Text);
    }

    /// <summary>本地审计时间保留墙上时间，参数显式使用 timestamp 而非驱动默认 timestamptz。</summary>
    [TestMethod]
    public async Task Insert_BindsLocalTimestampExplicitly()
    {
        using var fixture = new Fixture();
        await fixture.Repository.AddAsync(new PurchaseOrderLine());
        Assert.AreEqual(DateTimeKind.Unspecified, ((DateTime)fixture.Connection.Values["CreatedDate"]!).Kind);
        Assert.AreEqual(NpgsqlTypes.NpgsqlDbType.Timestamp, fixture.Connection.Types["CreatedDate"]);
    }

    /// <summary>基础数据的可空 SKU 经真实 Dapper 参数绑定保留为空，不自动补造关联。</summary>
    [TestMethod]
    public async Task SupplierProduct_PreservesNullableSku()
    {
        using var fixture = new Fixture();
        var user = new User();
        var repository = new PostgreSqlDapperRepository<SwiftExport.Core.Entities.Relations.SupplierProduct>(
            new RepositoryBaseContext(fixture.Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
        await repository.AddAsync(new SwiftExport.Core.Entities.Relations.SupplierProduct
        { SkuId = null, SupplierCode = "S01", ProductSku = "", SupplierSku = "SUP-01" });
        StringAssert.Contains(fixture.Connection.Text, "\"supplier_product\"");
        Assert.AreEqual(DBNull.Value, fixture.Connection.Values["SkuId"]);
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>构造单会话仓储，不读取运行配置或连接任何数据库。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly RecordingConnection Connection = new();
        public readonly DbSession Session;
        public readonly PostgreSqlDapperRepository<PurchaseOrderLine> Repository;
        public Fixture()
        {
            Session = new DbSession(new Factory(Connection));
            Session.Transaction = Connection.BeginTransaction();
            var user = new User();
            Repository = new(new RepositoryBaseContext(Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
        }
        public void Dispose() => Session.Dispose();
    }

    /// <summary>向会话注入不连接网络的测试连接。</summary>
    private sealed class Factory(RecordingConnection connection) : IDbConnectionFactory
    {
        public IDbConnection CreateConnection() => connection;
    }

    /// <summary>提供审计身份。</summary>
    private sealed class User : ICurrentUser
    {
        public Guid? UserId => Guid.Empty;
        public string UserName => "映射测试";
        public bool IsAuthenticated => true;
    }

    /// <summary>记录 Dapper 实际发出的命令及参数，数据读取由测试显式提供。</summary>
    private sealed class RecordingConnection : DbConnection
    {
        public string Text = "";
        public Dictionary<string, object?> Values = new();
        public Dictionary<string, NpgsqlTypes.NpgsqlDbType> Types = new();
        public DbTransaction? LastTransaction;
        public readonly List<(string Sql, Dictionary<string, object?> Values, DbTransaction? Transaction)> Commands = [];
        public int Commits;
        public int Rollbacks;
        public int TransactionDisposals;
        public Func<int>? ExecuteWrite;
        public DataTable Rows = new();
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "in-memory-test";
        public override string DataSource => "none";
        public override string ServerVersion => "test";
        public override ConnectionState State => ConnectionState.Open;
        public override void Open() { }
        public override void Close() { }
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new Transaction(this);
        protected override DbCommand CreateDbCommand() => new Command(this);

        /// <summary>仅提供事务身份，不执行提交或回滚到数据库。</summary>
        private sealed class Transaction(RecordingConnection connection) : DbTransaction
        {
            public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            protected override DbConnection DbConnection => connection;
            public override void Commit() => connection.Commits++;
            public override void Rollback() => connection.Rollbacks++;
            protected override void Dispose(bool disposing)
            { if (disposing) connection.TransactionDisposals++; base.Dispose(disposing); }
        }

        /// <summary>使用提供者参数容器兼容 Dapper 参数绑定，但完全接管执行，禁止网络访问。</summary>
        private sealed class Command(RecordingConnection connection) : DbCommand
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
            public override int ExecuteNonQuery() { Capture(); return connection.ExecuteWrite?.Invoke() ?? 1; }
            public override object ExecuteScalar() { Capture(); return 1; }
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            { Capture(); return connection.Rows.CreateDataReader(); }
            private void Capture()
            {
                connection.Text = CommandText;
                connection.Types = parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => p.NpgsqlDbType);
                connection.LastTransaction = DbTransaction;
                connection.Values = parameters.Parameters.Cast<NpgsqlParameter>()
                    .ToDictionary(parameter => parameter.ParameterName, parameter => (object?)parameter.Value);
                connection.Commands.Add((CommandText, connection.Values, DbTransaction));
            }
            protected override void Dispose(bool disposing)
            { if (disposing) parameters.Dispose(); base.Dispose(disposing); }
        }
    }
}
