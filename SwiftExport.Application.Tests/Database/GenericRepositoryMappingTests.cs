using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
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
public sealed class GenericRepositoryMappingTests
{
    /// <summary>新增绑定实体 Quantity 参数，物理 INSERT 使用 QTY，且沿用调用方事务。</summary>
    [TestMethod]
    public async Task Insert_UsesMappedColumnAndPropertyParameter()
    {
        using var fixture = new Fixture();
        var entity = new PurchaseOrderLine { Quantity = 12.5m };
        var result = await fixture.Repository.AddAsync(entity);
        Assert.IsTrue(result.Success);
        StringAssert.Contains(fixture.Connection.Text, "INSERT INTO [dbo].[PurchaseOrderLine]");
        StringAssert.Contains(fixture.Connection.Text, "[QTY]");
        Assert.IsFalse(fixture.Connection.Text.Contains("[Quantity]", StringComparison.Ordinal));
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
        StringAssert.Contains(fixture.Connection.Text, "[QTY] AS [Quantity]");
        StringAssert.Contains(fixture.Connection.Text, "WITH (UPDLOCK, ROWLOCK)");
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
        StringAssert.Contains(fixture.Connection.Text, "[QTY] = @Quantity");
        StringAssert.Contains(fixture.Connection.Text, "[Version] = [Version] + 1");
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
        StringAssert.Contains(fixture.Connection.Text, "[IsDeleted] = 1");
        StringAssert.Contains(fixture.Connection.Text, "[Version] = @Version");
        Assert.IsTrue((await fixture.Repository.DeleteAsync(Guid.NewGuid(), 3, false)).Success);
        StringAssert.Contains(fixture.Connection.Text, "DELETE FROM [dbo].[PurchaseOrderLine]");
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>构造单会话仓储，不读取运行配置或连接任何数据库。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly RecordingConnection Connection = new();
        public readonly DbSession Session;
        public readonly DapperRepository<PurchaseOrderLine> Repository;
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
        public DbTransaction? LastTransaction;
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
            public override void Commit() { }
            public override void Rollback() { }
        }

        /// <summary>使用提供者参数容器兼容 Dapper 参数绑定，但完全接管执行，禁止网络访问。</summary>
        private sealed class Command(RecordingConnection connection) : DbCommand
        {
            private readonly SqlCommand parameters = new();
            [AllowNull] public override string CommandText { get; set; } = "";
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; } = connection;
            protected override DbTransaction? DbTransaction { get; set; }
            protected override DbParameterCollection DbParameterCollection => parameters.Parameters;
            protected override DbParameter CreateDbParameter() => new SqlParameter();
            public override void Cancel() { }
            public override void Prepare() { }
            public override int ExecuteNonQuery() { Capture(); return 1; }
            public override object ExecuteScalar() { Capture(); return 1; }
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            { Capture(); return connection.Rows.CreateDataReader(); }
            private void Capture()
            {
                connection.Text = CommandText;
                connection.LastTransaction = DbTransaction;
                connection.Values = parameters.Parameters.Cast<SqlParameter>()
                    .ToDictionary(parameter => parameter.ParameterName, parameter => (object?)parameter.Value);
            }
            protected override void Dispose(bool disposing)
            { if (disposing) parameters.Dispose(); base.Dispose(disposing); }
        }
    }
}
