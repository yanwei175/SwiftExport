using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Auth.Development;

namespace SwiftExport.Application.Tests.Database;

/// <summary>无网络地观察真实 Dapper 标量调用；仅验证参数/边界，不模拟 PostgreSQL 比较或账号资格验收。</summary>
[TestClass]
public sealed class PostgreSqlDevUserQueryMigrationTests
{
    /// <summary>所有原输入作为一个明确 text 参数保留，包括空白和 SQL 特殊字符；事务和取消不透传。</summary>
    [TestMethod]
    [DataRow("dev")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(" Dev ")]
    [DataRow("dev\t")]
    [DataRow("' OR TRUE;--")]
    [DataRow(null)]
    public async Task Input_IsBoundUnchangedWithoutTransaction(string? input)
    {
        var connection = new RecordingConnection { Scalar = Guid.NewGuid() };
        using var session = new DbSession(new Factory(connection));
        session.Transaction = connection.BeginTransaction();
        Assert.AreEqual((Guid)connection.Scalar, await new PostgreSqlDevUserQuery(session).FindUidAsync(input!));
        Assert.AreEqual(1, connection.Calls); Assert.AreEqual(1, connection.Parameters.Length);
        Assert.AreEqual("UserName", connection.Parameters[0].ParameterName);
        Assert.AreEqual(input is null ? DBNull.Value : input, connection.Parameters[0].Value);
        Assert.AreEqual(NpgsqlDbType.Text, connection.Parameters[0].NpgsqlDbType);
        Assert.IsNull(connection.Transaction); Assert.IsFalse(connection.Token.CanBeCanceled);
        Assert.AreEqual(1, connection.Begins, "只有测试自己的父事务，查询不得自建事务。");
        Assert.IsNotNull(session.Transaction); Assert.AreEqual(0, connection.Commits);
        StringAssert.Contains(connection.Sql, "SELECT \"uid\"");
        StringAssert.Contains(connection.Sql, "FROM \"public\".\"sys_user\"");
        StringAssert.Contains(connection.Sql, "rtrim(\"user_name\", ' ') COLLATE public.erp_dictionary_ci");
        StringAssert.Contains(connection.Sql, "rtrim(@UserName, ' ') COLLATE public.erp_dictionary_ci");
        StringAssert.Contains(connection.Sql, "\"is_active\" = TRUE");
        StringAssert.Contains(connection.Sql, "COALESCE(\"is_deleted\", FALSE) = FALSE");
        StringAssert.Contains(connection.Sql, "LIMIT 1");
        Assert.IsFalse(connection.Sql.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(connection.Sql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(connection.Sql.Contains("NOLOCK", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(connection.Sql.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>无值或数据库NULL都是可空缺失，不转换空Guid、不缓存负结果。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MissingScalar_ReturnsNullAndQueriesAgain(bool dbNull)
    {
        var connection = new RecordingConnection { Scalar = dbNull ? DBNull.Value : null };
        using var session = new DbSession(new Factory(connection)); var query = new PostgreSqlDevUserQuery(session);
        Assert.IsNull(await query.FindUidAsync("missing")); Assert.IsNull(await query.FindUidAsync("missing"));
        Assert.AreEqual(2, connection.Calls); Assert.AreEqual(0, connection.Begins);
    }

    /// <summary>数据库和取消异常实例原样传播，无吞错、自动重试或降级身份。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Failure_PropagatesOriginalWithoutRetry(bool canceled)
    {
        var failure = canceled ? (Exception)new OperationCanceledException() : new InvalidOperationException("离线执行失败");
        var connection = new RecordingConnection { Failure = failure };
        using var session = new DbSession(new Factory(connection));
        Exception? observed = null;
        try { await new PostgreSqlDevUserQuery(session).FindUidAsync("dev"); } catch (Exception error) { observed = error; }
        Assert.AreSame(failure, observed); Assert.AreEqual(1, connection.Calls); Assert.AreEqual(0, connection.Begins);
    }

    /// <summary>为原生会话提供无网络连接，测试不读取任何运行配置。</summary>
    private sealed class Factory(RecordingConnection connection) : IDbConnectionFactory
    {
        /// <summary>返回同一明确记录连接。</summary>
        public IDbConnection CreateConnection() => connection;
    }

    /// <summary>只记录 ADO.NET 标量边界，不提供 SQL 引擎或网络功能。</summary>
    private sealed class RecordingConnection : DbConnection
    {
        internal object? Scalar; internal Exception? Failure; internal int Calls; internal int Begins; internal int Commits;
        internal string Sql = ""; internal NpgsqlParameter[] Parameters = []; internal DbTransaction? Transaction; internal CancellationToken Token { get; set; }
        [AllowNull] public override string ConnectionString { get; set; } = "";
        /// <summary>标识无网络测试库。</summary>
        public override string Database => "offline";
        /// <summary>不提供真实实例。</summary>
        public override string DataSource => "none";
        /// <summary>标识测试连接版本。</summary>
        public override string ServerVersion => "test";
        /// <summary>保持会话可观察的打开状态。</summary>
        public override ConnectionState State => ConnectionState.Open;
        /// <summary>无网络打开。</summary>
        public override void Open() { }
        /// <summary>无网络关闭。</summary>
        public override void Close() { }
        /// <summary>拒绝改变库。</summary>
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        /// <summary>记录调用方创建事务次数。</summary>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) { Begins++; return new TransactionStub(this); }
        /// <summary>提供记录命令。</summary>
        protected override DbCommand CreateDbCommand() => new ScalarCommand(this);

        /// <summary>仅提供父事务身份及提交次数，不写数据库。</summary>
        private sealed class TransactionStub(RecordingConnection connection) : DbTransaction
        {
            /// <summary>测试父事务的声明隔离。</summary>
            public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            /// <summary>绑定测试连接。</summary>
            protected override DbConnection DbConnection => connection;
            /// <summary>记录非预期提交。</summary>
            public override void Commit() => connection.Commits++;
            /// <summary>无网络回滚。</summary>
            public override void Rollback() { }
        }

        /// <summary>用真实Npgsql参数容器观察Dapper绑定，执行永远由记录连接截获。</summary>
        private sealed class ScalarCommand(RecordingConnection connection) : DbCommand
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
            /// <summary>创建真实参数，测试显式text绑定。</summary>
            protected override DbParameter CreateDbParameter() => new NpgsqlParameter();
            /// <summary>无网络取消。</summary>
            public override void Cancel() { }
            /// <summary>拒绝未授权预编译路径。</summary>
            public override void Prepare() => throw new NotSupportedException();
            /// <summary>拒绝任何写命令。</summary>
            public override int ExecuteNonQuery() => throw new NotSupportedException();
            /// <summary>同步标量也沿同一观察边界。</summary>
            public override object? ExecuteScalar() => ExecuteScalarAsync(default).GetAwaiter().GetResult();
            /// <summary>记录真实Dapper输入后返回预定值或原异常，不解释SQL。</summary>
            public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
            {
                connection.Calls++; connection.Sql = CommandText; connection.Transaction = DbTransaction; connection.Token = cancellationToken;
                connection.Parameters = parameters.Parameters.Cast<NpgsqlParameter>().Select(x => x.Clone()).ToArray();
                return connection.Failure is null ? Task.FromResult(connection.Scalar) : Task.FromException<object?>(connection.Failure);
            }
            /// <summary>查询仅允许标量，不伪造结果集。</summary>
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
            /// <summary>释放参数容器，保留连接归会话释放。</summary>
            protected override void Dispose(bool disposing) { if (disposing) parameters.Dispose(); base.Dispose(disposing); }
        }
    }
}

