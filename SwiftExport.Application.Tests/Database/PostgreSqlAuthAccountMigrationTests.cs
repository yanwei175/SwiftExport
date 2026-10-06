using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auth.Accounts;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Auth.Accounts;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>以独立无网络记录连接验证八方法真实 Dapper 调用；不替代 PostgreSQL 快照和唯一索引验收。</summary>
[TestClass]
[TestCategory("PostgreSqlAuthAccountOffline")]
public sealed class PostgreSqlAuthAccountMigrationTests
{
    /// <summary>两个凭据读取均映射完整字段、保留空值并沿用事务；用户名注入文本只作为参数。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Read_MapsCredentialsAndBindsIdentity(bool byName)
    {
        using var fixture = new Fixture();
        var user = Account();
        fixture.Connection.Readers.Enqueue(UserRows(user));
        var name = " Name'; DROP TABLE sys_user;-- ";
        var actual = byName
            ? await fixture.Repository.GetActiveUserByNameAsync(name)
            : await fixture.Repository.GetActiveUserByIdAsync(user.Uid);
        Assert.IsNotNull(actual);
        Assert.AreEqual(user.Uid, actual.Uid);
        Assert.AreEqual(user.Version, actual.Version);
        Assert.AreEqual(user.PasswordHash, actual.PasswordHash);
        Assert.AreEqual(user.CredentialVersion, actual.CredentialVersion);
        Assert.AreEqual(user.CreatedDate, actual.CreatedDate);
        Assert.AreEqual(user.UpdatedBy, actual.UpdatedBy);
        Assert.IsNull(actual.Email);
        Assert.AreEqual(user.PhoneNumber, actual.PhoneNumber);
        Assert.AreEqual(user.UserName, actual.UserName);
        Assert.AreEqual(user.NickName, actual.NickName);
        Assert.IsTrue(actual.IsActive);
        Assert.IsFalse(actual.IsDeleted);
        Assert.AreEqual(byName ? (object)name : user.Uid, fixture.Connection.Values[byName ? "UserName" : "UserId"]);
        Assert.IsFalse(fixture.Connection.Text.Contains(name, StringComparison.Ordinal));
        StringAssert.Contains(fixture.Connection.Text, "\"credential_version\" AS \"CredentialVersion\"");
        if (byName) StringAssert.Contains(fixture.Connection.Text, "rtrim(@UserName, ' ') COLLATE public.erp_dictionary_ci");
        AssertBoundary(fixture, 1);
    }

    /// <summary>没有活动行返回空，不创建隐式业务事务。</summary>
    [TestMethod]
    public async Task Read_MissingUserWithoutTransaction()
    {
        using var fixture = new Fixture(false);
        fixture.Connection.Readers.Enqueue(UserRows(null));
        Assert.IsNull(await fixture.Repository.GetActiveUserByIdAsync(Guid.NewGuid()));
        AssertBoundary(fixture, 1);
    }

    /// <summary>管理员、占用和凭据有效性分别绑定身份参数，并返回数据库真假结果。</summary>
    [TestMethod]
    [DataRow("admin", true)]
    [DataRow("admin", false)]
    [DataRow("name", true)]
    [DataRow("name", false)]
    [DataRow("credential", true)]
    [DataRow("credential", false)]
    public async Task Exists_ReturnsProviderBoolean(string operation, bool expected)
    {
        using var fixture = new Fixture();
        fixture.Connection.Scalars.Enqueue(expected);
        var id = Guid.NewGuid();
        var actual = operation switch
        {
            "admin" => await fixture.Repository.IsAdminAsync(id),
            "name" => await fixture.Repository.IsUserNameInUseAsync("停用账号"),
            _ => await fixture.Repository.IsCredentialVersionValidAsync(id, 4)
        };
        Assert.AreEqual(expected, actual);
        StringAssert.Contains(fixture.Connection.Text, "SELECT EXISTS");
        if (operation == "name") Assert.IsFalse(fixture.Connection.Text.Contains("is_active", StringComparison.Ordinal));
        if (operation == "admin") StringAssert.Contains(fixture.Connection.Text, "r.role_code = 'SYSTEM_ADMIN'");
        if (operation == "credential") Assert.AreEqual(4, fixture.Connection.Values["CredentialVersion"]);
        AssertBoundary(fixture, 1);
    }

    /// <summary>创建保留重名优先、最终授权和晚到重名分类；没有重名异常破坏调用方事务。</summary>
    [TestMethod]
    [DataRow("duplicate", AuthUserCreateStatus.UserNameInUse, true)]
    [DataRow("created", AuthUserCreateStatus.Created, true)]
    [DataRow("late", AuthUserCreateStatus.UserNameInUse, true)]
    [DataRow("unauthorized", AuthUserCreateStatus.ActorNotAdmin, true)]
    [DataRow("created", AuthUserCreateStatus.Created, false)]
    public async Task Create_PreservesStatusAndTransaction(string path, AuthUserCreateStatus expected, bool transaction)
    {
        using var fixture = new Fixture(transaction);
        var user = Account();
        var actor = Guid.NewGuid();
        fixture.Connection.Scalars.Enqueue(path == "duplicate");
        if (path != "duplicate")
        {
            fixture.Connection.Readers.Enqueue(IdRows(path == "created" ? user.Uid : null));
            if (path != "created") fixture.Connection.Scalars.Enqueue(path == "late");
        }
        Assert.AreEqual(expected, await fixture.Repository.CreateUserByAdminAsync(actor, user));
        AssertBoundary(fixture, path == "duplicate" ? 1 : path == "created" ? 2 : 3);
        Assert.IsNotNull(user.CreatedDate);
        if (path == "duplicate") return;
        var insert = fixture.Connection.Commands[1];
        StringAssert.Contains(insert.Sql, "ON CONFLICT ((rtrim(user_name, ' ')) COLLATE public.erp_dictionary_ci)");
        StringAssert.Contains(insert.Sql, "WHERE is_deleted = FALSE DO NOTHING");
        StringAssert.Contains(insert.Sql, "WHERE EXISTS");
        Assert.AreEqual(actor, insert.Values["ActorUserId"]);
        Assert.AreEqual(user.Uid, insert.Values["Uid"]);
        Assert.AreEqual(user.Version, insert.Values["Version"]);
        Assert.AreEqual(user.CredentialVersion, insert.Values["CredentialVersion"]);
        Assert.AreEqual(DateTimeKind.Unspecified, ((DateTime)insert.Values["CreatedDate"]!).Kind);
    }

    /// <summary>非用户名唯一失败必须原样传播，不归类为重名且不擅自清理事务。</summary>
    [TestMethod]
    public async Task Create_PropagatesOtherUniqueFailure()
    {
        using var fixture = new Fixture();
        fixture.Connection.Scalars.Enqueue(false);
        var failure = new PostgresException("专用 Uid 冲突", "ERROR", "ERROR", "23505", constraintName: "pk_sys_user");
        fixture.Connection.BeforeExecute = sql => { if (sql.Contains("INSERT INTO", StringComparison.Ordinal)) throw failure; };
        var actual = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
            fixture.Repository.CreateUserByAdminAsync(Guid.NewGuid(), Account()));
        Assert.AreSame(failure, actual);
        Assert.AreEqual(0, fixture.Connection.Rollbacks);
        Assert.IsNotNull(fixture.Session.Transaction);
    }

    /// <summary>本人改密绑定原双版本与 timestamp 审计，数据库成功或零行均不回填输入版本。</summary>
    [TestMethod]
    [DataRow(1, true)]
    [DataRow(0, false)]
    public async Task OwnPassword_PreservesVersionsAndAudit(int rows, bool expected)
    {
        using var fixture = new Fixture();
        fixture.Connection.ExecuteWrite = () => rows;
        var user = Account();
        Assert.AreEqual(expected, await fixture.Repository.UpdateOwnPasswordAsync(user));
        Assert.AreEqual(3, user.Version);
        Assert.AreEqual(4, user.CredentialVersion);
        Assert.AreEqual(3, fixture.Connection.Values["Version"]);
        Assert.AreEqual(4, fixture.Connection.Values["CredentialVersion"]);
        Assert.AreEqual("映射测试", fixture.Connection.Values["UpdatedBy"]);
        Assert.AreEqual(NpgsqlDbType.Timestamp, fixture.Connection.Types["UpdatedDate"]);
        StringAssert.Contains(fixture.Connection.Text, "credential_version = credential_version + 1");
        StringAssert.Contains(fixture.Connection.Text, "version = version + 1");
        AssertBoundary(fixture, 1);
    }

    /// <summary>重置按原顺序短路，最终更新必须重新包含授权和目标非管理员条件。</summary>
    [TestMethod]
    [DataRow("actor", AdminPasswordResetStatus.ActorNotAdmin, 1)]
    [DataRow("missing", AdminPasswordResetStatus.TargetNotFound, 2)]
    [DataRow("admin", AdminPasswordResetStatus.TargetIsAdmin, 3)]
    [DataRow("updated", AdminPasswordResetStatus.Updated, 4)]
    [DataRow("conflict", AdminPasswordResetStatus.ConcurrencyConflict, 4)]
    public async Task Reset_PreservesPrecedenceAndFinalRecheck(string path, AdminPasswordResetStatus expected, int commands)
    {
        using var fixture = new Fixture();
        var user = Account();
        fixture.Connection.Scalars.Enqueue(path != "actor");
        if (path != "actor") fixture.Connection.Readers.Enqueue(UserRows(path == "missing" ? null : user));
        if (path is not "actor" and not "missing") fixture.Connection.Scalars.Enqueue(path == "admin");
        fixture.Connection.ExecuteWrite = () => path == "conflict" ? 0 : 1;
        Assert.AreEqual(expected, await fixture.Repository.ResetNonAdminPasswordAsync(Guid.NewGuid(), user));
        AssertBoundary(fixture, commands);
        if (commands != 4) return;
        StringAssert.Contains(fixture.Connection.Text, "AND EXISTS");
        StringAssert.Contains(fixture.Connection.Text, "AND NOT EXISTS");
        StringAssert.Contains(fixture.Connection.Text, "actor.uid = target.uid");
        StringAssert.Contains(fixture.Connection.Text, "target.credential_version = @CredentialVersion");
        Assert.AreEqual(3, user.Version);
        Assert.AreEqual(4, user.CredentialVersion);
        Assert.AreEqual(NpgsqlDbType.Timestamp, fixture.Connection.Types["UpdatedDate"]);
    }

    /// <summary>取消令牌到达实际 ADO 命令；已取消请求不执行任何数据库命令。</summary>
    [TestMethod]
    public async Task Cancellation_ReachesProviderAndStopsExecution()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Connection.Scalars.Enqueue(true);
        Assert.IsTrue(await fixture.Repository.IsAdminAsync(Guid.NewGuid(), cancellation.Token));
        Assert.AreEqual(cancellation.Token, fixture.Connection.LastCancellation);
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            fixture.Repository.UpdateOwnPasswordAsync(Account(), cancellation.Token));
        AssertBoundary(fixture, 1);
    }

    /// <summary>验证每个分阶段命令借用同一事务，无自行提交、回滚、释放或新增事务。</summary>
    private static void AssertBoundary(Fixture fixture, int commands)
    {
        Assert.HasCount(commands, fixture.Connection.Commands);
        foreach (var command in fixture.Connection.Commands) Assert.AreSame(fixture.Session.Transaction, command.Transaction);
        Assert.AreEqual(fixture.Session.Transaction is null ? 0 : 1, fixture.Connection.Begins);
        Assert.AreEqual(0, fixture.Connection.Commits);
        Assert.AreEqual(0, fixture.Connection.Rollbacks);
        Assert.AreEqual(0, fixture.Connection.TransactionDisposals);
    }

    /// <summary>构造完整可空凭据结果，避免仅以 SQL 字符串断言代替映射验证。</summary>
    private static DataTable UserRows(SysUser? user)
    {
        var table = new DataTable();
        foreach (var property in typeof(SysUser).GetProperties())
            table.Columns.Add(property.Name, Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);
        if (user is not null) table.Rows.Add(typeof(SysUser).GetProperties().Select(p => p.GetValue(user) ?? DBNull.Value).ToArray());
        return table;
    }

    /// <summary>模拟 RETURNING 的一行或零行，不伪造数据库冲突语义。</summary>
    private static DataTable IdRows(Guid? id)
    {
        var table = new DataTable();
        table.Columns.Add("uid", typeof(Guid));
        if (id.HasValue) table.Rows.Add(id.Value);
        return table;
    }

    /// <summary>生成带旧版本和固定审计的测试账号，不涉及真实密码。</summary>
    private static SysUser Account() => new()
    {
        UserName = "测试账号", NickName = "昵称", PasswordHash = "测试哈希", Version = 3, CredentialVersion = 4,
        Email = null, PhoneNumber = "123", CreatedDate = new DateTime(2026, 1, 2), CreatedBy = "创建者",
        UpdatedDate = new DateTime(2026, 1, 3), UpdatedBy = "更新者"
    };

    /// <summary>每个场景拥有独立记录连接及可选外层事务，不依赖其他类的私有夹具。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly RecordingConnection Connection = new();
        public readonly DbSession Session;
        public readonly PostgreSqlAuthRepository Repository;
        public Fixture(bool transaction = true)
        {
            Session = new DbSession(new Factory(Connection));
            if (transaction) Session.Transaction = Connection.BeginTransaction();
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
        public readonly Queue<object> Scalars = new();
        public readonly Queue<DataTable> Readers = new();
        public CancellationToken LastCancellation;
        public Action<string>? BeforeExecute;
        public int Begins;
        public int Commits;
        public int Rollbacks;
        public int TransactionDisposals;
        public Func<int>? ExecuteWrite;
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "in-memory-test";
        public override string DataSource => "none";
        public override string ServerVersion => "test";
        public override ConnectionState State => ConnectionState.Open;
        public override void Open() { }
        public override void Close() { }
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) { Begins++; return new Transaction(this); }
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
            public override object ExecuteScalar() { Capture(); return connection.Scalars.Dequeue(); }
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            { Capture(); return connection.Readers.Dequeue().CreateDataReader(); }
            public override Task<int> ExecuteNonQueryAsync(CancellationToken token)
            { connection.LastCancellation = token; token.ThrowIfCancellationRequested(); return Task.FromResult(ExecuteNonQuery()); }
            public override Task<object?> ExecuteScalarAsync(CancellationToken token)
            { connection.LastCancellation = token; token.ThrowIfCancellationRequested(); return Task.FromResult<object?>(ExecuteScalar()); }
            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
            { connection.LastCancellation = token; token.ThrowIfCancellationRequested(); return Task.FromResult(ExecuteDbDataReader(behavior)); }
            private void Capture()
            {
                connection.BeforeExecute?.Invoke(CommandText);
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

