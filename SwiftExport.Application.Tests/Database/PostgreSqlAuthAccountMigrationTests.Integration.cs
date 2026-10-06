using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Application.Services.Auth.Passwords;
using SwiftExport.Application.Services.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Auth.Session;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auth.Accounts;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Auth.Accounts;
using SwiftExport.Infrastructure.Repositories.Auth.Permissions;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Services.Auth;
using SwiftExport.WebApi.Authentication;

namespace SwiftExport.Application.Tests.Database;

/// <summary>仅由主控串行运行的账号实库场景；回滚夹具或精确清理随机 Uid，不初始化、重置或修改既有角色。</summary>
[TestClass]
[DoNotParallelize]
[TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlAuthAccountMigrationIntegrationTests
{
    /// <summary>真实登录和 me 读取权限投影；改密递增双版本并使旧 JWT 凭据立即失效。</summary>
    [TestMethod]
    public async Task LoginMeAndPassword_InvalidateOldJwt()
    {
        using var fixture = new Fixture();
        var hasher = new BcryptPasswordHasher();
        fixture.Target.PasswordHash = hasher.Hash("旧密码 abc");
        await fixture.SeedAsync();
        var sessionService = new AuthSessionService(fixture.Repository, new Identity(fixture.Target.Uid),
            hasher, new PermissionEvaluationService(new PostgreSqlUserPermissionRepository(fixture.Context)));
        var login = await sessionService.AuthenticateAsync(new LoginRequestDto
            { UserName = " " + fixture.Target.UserName + " ", Password = "旧密码 abc" });
        Assert.IsTrue(login.Success);
        var me = await sessionService.GetCurrentUserAsync();
        Assert.IsTrue(me.Success);
        Assert.AreEqual(fixture.Target.Uid.ToString(), me.Data.UserId);
        Assert.IsFalse(me.Data.IsAdmin);
        Assert.HasCount(0, me.Data.RoleCodes);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:JwtKey"] = "isolated-account-test-key-for-hmac-sha256"
        }).Build();
        var token = new AuthTokenFactory(configuration).Create(login.Data);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token.Access_token);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "test"));
        var validator = new CredentialVersionTokenValidator(sessionService);
        Assert.IsTrue(await validator.ValidateAsync(principal));
        var originalVersion = fixture.Target.Version;
        fixture.Target.PasswordHash = hasher.Hash("新密码 abc");
        Assert.IsTrue(await fixture.Repository.UpdateOwnPasswordAsync(fixture.Target));
        Assert.IsFalse(await validator.ValidateAsync(principal));
        var updated = await fixture.Repository.GetActiveUserByIdAsync(fixture.Target.Uid);
        Assert.IsNotNull(updated);
        Assert.AreEqual(originalVersion + 1, updated.Version);
        Assert.AreEqual(fixture.Target.CredentialVersion + 1, updated.CredentialVersion);
        Assert.AreEqual("账号迁移实库夹具", updated.UpdatedBy);
        Assert.IsTrue(hasher.Verify("新密码 abc", updated.PasswordHash));
        Assert.IsFalse(await fixture.Repository.UpdateOwnPasswordAsync(fixture.Target));
    }

    /// <summary>停用及软删分别阻断认证、改密和凭据验证；只有未删除账号占用用户名。</summary>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, true)]
    public async Task InvalidAccount_IsUnavailable(bool active, bool deleted)
    {
        using var fixture = new Fixture();
        fixture.Target.IsActive = active;
        fixture.Target.IsDeleted = deleted;
        await fixture.SeedAsync();
        Assert.IsNull(await fixture.Repository.GetActiveUserByNameAsync(fixture.Target.UserName));
        Assert.IsNull(await fixture.Repository.GetActiveUserByIdAsync(fixture.Target.Uid));
        Assert.AreEqual(!deleted, await fixture.Repository.IsUserNameInUseAsync(fixture.Target.UserName));
        Assert.IsFalse(await fixture.Repository.IsCredentialVersionValidAsync(fixture.Target.Uid, 1));
        Assert.IsFalse(await fixture.Repository.UpdateOwnPasswordAsync(fixture.Target));
        Assert.IsFalse(await fixture.Repository.IsAdminAsync(fixture.Target.Uid));
    }

    /// <summary>用户名查询和唯一冲突沿用 ICU、ASCII 尾空格与重音边界，重名后外层事务仍可查询。</summary>
    [TestMethod]
    [DataRow("case", true)]
    [DataRow("space", true)]
    [DataRow("width", true)]
    [DataRow("accent", false)]
    public async Task UserNameComparison_AndConflictKeepTransactionUsable(string comparison, bool equal)
    {
        using var fixture = new Fixture();
        fixture.Target.UserName += "Unit";
        await fixture.SeedAsync();
        var alternate = comparison switch
        {
            "case" => fixture.Target.UserName.ToLowerInvariant(),
            "space" => fixture.Target.UserName + " ",
            "width" => fixture.Target.UserName[..^4] + "Ｕｎｉｔ",
            _ => fixture.Target.UserName[..^4] + "Unít"
        };
        Assert.AreEqual(equal, await fixture.Repository.IsUserNameInUseAsync(alternate));
        Assert.AreEqual(equal, await fixture.Repository.GetActiveUserByNameAsync(alternate) is not null);
        fixture.Created.UserName = alternate;
        Assert.AreEqual(equal ? AuthUserCreateStatus.UserNameInUse : AuthUserCreateStatus.Created,
            await fixture.Repository.CreateUserByAdminAsync(fixture.Actor.Uid, fixture.Created));
        Assert.AreEqual(1, await fixture.Session.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT 1;", transaction: fixture.Session.Transaction)));
    }

    /// <summary>重置保留每个结果枚举；成功后版本递增、旧凭据失效，陈旧输入返回并发冲突。</summary>
    [TestMethod]
    public async Task Reset_StatusesAndCredentialVersions()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        Assert.AreEqual(AdminPasswordResetStatus.ActorNotAdmin,
            await fixture.Repository.ResetNonAdminPasswordAsync(fixture.Target.Uid, fixture.Target));
        Assert.AreEqual(AdminPasswordResetStatus.TargetNotFound,
            await fixture.Repository.ResetNonAdminPasswordAsync(fixture.Actor.Uid, fixture.Created));
        Assert.AreEqual(AdminPasswordResetStatus.TargetIsAdmin,
            await fixture.Repository.ResetNonAdminPasswordAsync(fixture.Actor.Uid, fixture.Actor));
        fixture.Target.PasswordHash = "新专用哈希";
        Assert.AreEqual(AdminPasswordResetStatus.Updated,
            await fixture.Repository.ResetNonAdminPasswordAsync(fixture.Actor.Uid, fixture.Target));
        Assert.IsFalse(await fixture.Repository.IsCredentialVersionValidAsync(fixture.Target.Uid, 1));
        Assert.IsTrue(await fixture.Repository.IsCredentialVersionValidAsync(fixture.Target.Uid, 2));
        Assert.AreEqual(AdminPasswordResetStatus.ConcurrencyConflict,
            await fixture.Repository.ResetNonAdminPasswordAsync(fixture.Actor.Uid, fixture.Target));
    }

    /// <summary>在分阶段预判后、最终语句前由另一连接提交变化，写入重新拒绝失效授权及管理员目标。</summary>
    [TestMethod]
    [DataRow("create-revoke")]
    [DataRow("reset-revoke")]
    [DataRow("reset-promote")]
    [DataRow("reset-disable")]
    [DataRow("create-duplicate")]
    public async Task CommittedChangeBeforeWrite_IsRechecked(string change)
    {
        using var fixture = new Fixture(false);
        try
        {
            await fixture.SeedAsync();
            using var other = fixture.NewSession();
            var changed = false;
            fixture.Connection.BeforeExecute = async sql =>
            {
                var isWrite = change.StartsWith("create", StringComparison.Ordinal)
                    ? sql.Contains("INSERT INTO", StringComparison.Ordinal)
                    : sql.Contains("UPDATE", StringComparison.Ordinal);
                if (!isWrite || changed) return;
                changed = true;
                if (change.EndsWith("revoke", StringComparison.Ordinal))
                    await other.Connection.ExecuteAsync("UPDATE public.sys_user_role SET is_deleted = TRUE WHERE uid = @Uid;",
                        new { Uid = fixture.ActorRelation.Uid });
                else if (change == "reset-promote")
                    await Fixture.InsertAsync(Fixture.ContextFor(other), fixture.TargetRelation);
                else if (change == "reset-disable")
                    await other.Connection.ExecuteAsync("UPDATE public.sys_user SET is_active = FALSE WHERE uid = @Uid;",
                        new { fixture.Target.Uid });
                else
                {
                    fixture.Created.UserName = fixture.Created.UserName.ToLowerInvariant() + " ";
                    await Fixture.InsertAsync(Fixture.ContextFor(other), fixture.Created);
                }
            };
            if (change.StartsWith("create", StringComparison.Ordinal))
            {
                var requested = new SysUser { Uid = fixture.CandidateId, UserName = fixture.Created.UserName, PasswordHash = "候选哈希" };
                Assert.AreEqual(change == "create-duplicate" ? AuthUserCreateStatus.UserNameInUse : AuthUserCreateStatus.ActorNotAdmin,
                    await fixture.Repository.CreateUserByAdminAsync(fixture.Actor.Uid, requested));
                Assert.IsNull(await fixture.Repository.GetActiveUserByIdAsync(requested.Uid));
            }
            else
            {
                fixture.Target.PasswordHash = "禁止写入的哈希";
                Assert.AreEqual(AdminPasswordResetStatus.ConcurrencyConflict,
                    await fixture.Repository.ResetNonAdminPasswordAsync(fixture.Actor.Uid, fixture.Target));
                var stored = await fixture.Session.Connection.ExecuteScalarAsync<string>(
                    "SELECT password_hash FROM public.sys_user WHERE uid = @Uid;", new { fixture.Target.Uid });
                Assert.AreEqual("专用不可登录哈希", stored);
            }
            Assert.IsTrue(changed, "变化必须发生于最终写入开始之前。");
        }
        finally
        {
            fixture.Connection.BeforeExecute = null;
            await fixture.CleanupCommittedAsync();
        }
    }

    /// <summary>两个连接完成重名预查后同时提交等价用户名；定向索引只允许一个创建并保留另一方正常返回。</summary>
    [TestMethod]
    public async Task ConcurrentCreate_EquivalentNamesHaveSingleWinner()
    {
        using var fixture = new Fixture(false);
        try
        {
            await fixture.SeedAsync();
            using var otherConnection = fixture.NewInterceptConnection();
            using var other = new DbSession(new Factory(otherConnection));
            var secondRepository = new PostgreSqlAuthRepository(Fixture.ContextFor(other));
            var second = new SysUser
            {
                Uid = fixture.CandidateId, UserName = fixture.Created.UserName.ToLowerInvariant() + " ",
                PasswordHash = "第二候选哈希"
            };
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var arrivals = 0;
            Task Barrier(string sql)
            {
                if (!sql.Contains("INSERT INTO", StringComparison.Ordinal)) return Task.CompletedTask;
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                return ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            fixture.Connection.BeforeExecute = Barrier;
            otherConnection.BeforeExecute = Barrier;
            var statuses = await Task.WhenAll(
                fixture.Repository.CreateUserByAdminAsync(fixture.Actor.Uid, fixture.Created),
                secondRepository.CreateUserByAdminAsync(fixture.Actor.Uid, second));
            fixture.Connection.BeforeExecute = null;
            otherConnection.BeforeExecute = null;
            Assert.AreEqual(2, arrivals);
            Assert.AreEqual(1, statuses.Count(status => status == AuthUserCreateStatus.Created));
            Assert.AreEqual(1, statuses.Count(status => status == AuthUserCreateStatus.UserNameInUse));
            Assert.AreEqual(1L, await other.Connection.ExecuteScalarAsync<long>("""
                SELECT count(*) FROM public.sys_user
                WHERE rtrim(user_name, ' ') COLLATE public.erp_dictionary_ci =
                      rtrim(@UserName, ' ') COLLATE public.erp_dictionary_ci AND is_deleted = FALSE;
                """, new { fixture.Created.UserName }));
        }
        finally
        {
            fixture.Connection.BeforeExecute = null;
            await fixture.CleanupCommittedAsync();
        }
    }

    /// <summary>管理员创建成功不附加角色，普通操作者无权创建；同 Uid 不同名称失败保留提供者错误。</summary>
    [TestMethod]
    public async Task Create_AuthorizationAndNonNameUniqueFailure()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        Assert.AreEqual(AuthUserCreateStatus.ActorNotAdmin,
            await fixture.Repository.CreateUserByAdminAsync(fixture.Target.Uid, fixture.Created));
        Assert.IsNull(await fixture.Repository.GetActiveUserByIdAsync(fixture.Created.Uid));
        Assert.AreEqual(AuthUserCreateStatus.Created,
            await fixture.Repository.CreateUserByAdminAsync(fixture.Actor.Uid, fixture.Created));
        Assert.IsFalse(await fixture.Repository.IsAdminAsync(fixture.Created.Uid));
        var duplicateId = new SysUser { Uid = fixture.Created.Uid, UserName = "__M09_" + Guid.NewGuid().ToString("N"), PasswordHash = "冲突哈希" };
        var error = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
            fixture.Repository.CreateUserByAdminAsync(fixture.Actor.Uid, duplicateId));
        Assert.AreEqual("23505", error.SqlState);
        // 此非用户名唯一错误会使外层事务失败，必须由本夹具 finally 回滚，不能伪装正常重名。
    }

    /// <summary>已取消命令不改密码；显式回滚后第二连接确认所有随机账号和关系均不存在。</summary>
    [TestMethod]
    public async Task CancellationAndRollback_LeaveNoFixtureRows()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            fixture.Repository.UpdateOwnPasswordAsync(fixture.Target, cancellation.Token));
        // Npgsql 取消可能结束事务，统一回滚本层事务后再由另一连接核对持久化结果。
        fixture.Session.Transaction!.Rollback();
        fixture.Session.Transaction.Dispose();
        fixture.Session.Transaction = null;
        using var observer = fixture.NewSession();
        Assert.AreEqual(0L, await observer.Connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM public.sys_user WHERE uid = ANY(@Ids);",
            new { Ids = new[] { fixture.Actor.Uid, fixture.Target.Uid } }));
        Assert.AreEqual(0L, await observer.Connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM public.sys_user_role WHERE uid = @Uid;", new { fixture.ActorRelation.Uid }));
    }

    /// <summary>两个连接同时写相同旧双版本只有一个成功，最终凭据版本只递增一次。</summary>
    [TestMethod]
    public async Task ConcurrentPasswordUpdates_HaveSingleWinner()
    {
        using var fixture = new Fixture(false);
        try
        {
            await fixture.SeedAsync();
            using var other = fixture.NewSession();
            var secondRepository = new PostgreSqlAuthRepository(Fixture.ContextFor(other));
            var first = await fixture.Repository.GetActiveUserByIdAsync(fixture.Target.Uid);
            var second = await secondRepository.GetActiveUserByIdAsync(fixture.Target.Uid);
            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            first.PasswordHash = "并发一";
            second.PasswordHash = "并发二";
            var results = await Task.WhenAll(fixture.Repository.UpdateOwnPasswordAsync(first),
                secondRepository.UpdateOwnPasswordAsync(second));
            Assert.AreEqual(1, results.Count(value => value));
            var stored = await fixture.Repository.GetActiveUserByIdAsync(fixture.Target.Uid);
            Assert.IsNotNull(stored);
            Assert.AreEqual(2, stored.Version);
            Assert.AreEqual(2, stored.CredentialVersion);
        }
        finally { await fixture.CleanupCommittedAsync(); }
    }

    /// <summary>在外层事务内真正触发晚到的定向用户名冲突，随后可继续查询并回滚整个事务。</summary>
    [TestMethod]
    public async Task LateNameConflict_DoesNotAbortOuterTransaction()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var inserted = false;
        fixture.Connection.BeforeExecute = async sql =>
        {
            if (!sql.Contains("INSERT INTO", StringComparison.Ordinal) || inserted) return;
            inserted = true;
            fixture.Connection.BeforeExecute = null;
            var rival = new SysUser { Uid = fixture.CandidateId, UserName = fixture.Created.UserName + " ", PasswordHash = "竞争哈希" };
            await Fixture.InsertAsync(fixture.Context, rival);
        };
        Assert.AreEqual(AuthUserCreateStatus.UserNameInUse,
            await fixture.Repository.CreateUserByAdminAsync(fixture.Actor.Uid, fixture.Created));
        Assert.IsTrue(inserted);
        Assert.AreEqual(1, await fixture.Session.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT 1;", transaction: fixture.Session.Transaction)));
        Assert.IsNull(await fixture.Repository.GetActiveUserByIdAsync(fixture.Created.Uid));
    }

    /// <summary>仅持有随机账号与关系；默认外层回滚，提交场景必须 finally 精确清理列明 Uid。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly InterceptConnection Connection;
        public readonly DbSession Session;
        public readonly RepositoryBaseContext Context;
        public readonly PostgreSqlAuthRepository Repository;
        public readonly SysUser Actor = NewAccount();
        public readonly SysUser Target = NewAccount();
        public readonly SysUser Created = NewAccount();
        public readonly Guid CandidateId = Guid.NewGuid();
        public readonly SysUserRole ActorRelation = new();
        public readonly SysUserRole TargetRelation = new();
        private readonly string _connectionString;

        /// <summary>目标严格借用故障库入口，不打印凭据；调用前不会初始化任何数据库结构。</summary>
        public Fixture(bool rollback = true)
        {
            _connectionString = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
            Connection = new InterceptConnection(_connectionString);
            Session = new DbSession(new Factory(Connection));
            if (rollback) Session.Transaction = Session.Connection.BeginTransaction();
            Context = ContextFor(Session);
            Repository = new(Context);
        }

        /// <summary>只读借用现存活动管理员角色；不创建或变更系统角色，只插入本夹具账号与随机关系。</summary>
        public async Task SeedAsync()
        {
            var role = await Session.Connection.QuerySingleAsync<Guid>(new CommandDefinition("""
                SELECT uid FROM public.sys_role
                WHERE role_code = 'SYSTEM_ADMIN' AND is_active = TRUE AND is_deleted = FALSE;
                """, transaction: Session.Transaction));
            ActorRelation.UserId = Actor.Uid;
            ActorRelation.RoleId = role;
            TargetRelation.UserId = Target.Uid;
            TargetRelation.RoleId = role;
            await InsertAsync(Context, Actor);
            await InsertAsync(Context, Target);
            await InsertAsync(Context, ActorRelation);
        }

        /// <summary>开第二连接用于已提交竞争，连接生命周期由测试负责。</summary>
        public DbSession NewSession() => new(new PostgreSqlConnectionFactory(_connectionString));

        /// <summary>使用夹具私有原始配置创建第二包装连接，避免已打开连接隐藏密码后的字符串复用。</summary>
        public InterceptConnection NewInterceptConnection() => new(_connectionString);

        /// <summary>仅删除明确列出的随机 Uid；不按名字清理，不修改角色或既有账号，无外键级联。</summary>
        public async Task CleanupCommittedAsync()
        {
            using var cleanup = NewSession();
            using var transaction = cleanup.Connection.BeginTransaction();
            var ids = new[] { Actor.Uid, Target.Uid, Created.Uid, CandidateId };
            var relations = new[] { ActorRelation.Uid, TargetRelation.Uid };
            await cleanup.Connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM public.sys_user_role WHERE uid = ANY(@Ids);", new { Ids = relations }, transaction));
            await cleanup.Connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM public.sys_user WHERE uid = ANY(@Ids);", new { Ids = ids }, transaction));
            transaction.Commit();
            Assert.AreEqual(0L, await cleanup.Connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM public.sys_user WHERE uid = ANY(@Ids);", new { Ids = ids }));
            Assert.AreEqual(0L, await cleanup.Connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM public.sys_user_role WHERE uid = ANY(@Ids);", new { Ids = relations }));
        }

        /// <summary>回滚本层拥有的事务并清空会话引用；无事务夹具的提交记录由 finally 精确清理。</summary>
        public void Dispose()
        {
            try
            {
                if (Session.Transaction is { } transaction)
                {
                    try { transaction.Rollback(); }
                    finally { transaction.Dispose(); Session.Transaction = null; }
                }
            }
            finally { Session.Dispose(); }
        }

        /// <summary>复用生产审计和参数映射，保留专用 Uid 和负向删除/启停状态。</summary>
        public static async Task InsertAsync<T>(RepositoryBaseContext context, T entity) where T : BaseEntity
        {
            context.AuditStamp.StampForInsert(entity);
            var metadata = PostgreSqlEntityMetadata.GetMetadata(typeof(T));
            var table = $"{PostgreSqlNames.Quote(metadata.SchemaName)}.{PostgreSqlNames.Quote(metadata.TableName)}";
            var columns = string.Join(", ", metadata.Columns.Select(column => PostgreSqlNames.Quote(column.ColumnName)));
            var parameters = string.Join(", ", metadata.Columns.Select(column => "@" + column.ParameterName));
            Assert.AreEqual(1, await context.Session.Connection.ExecuteAsync(new CommandDefinition(
                $"INSERT INTO {table} ({columns}) VALUES ({parameters});",
                PostgreSqlParameters.Entity(entity, metadata), context.Session.Transaction)));
        }

        /// <summary>为会话创建固定测试审计身份，不冒用既有登录用户。</summary>
        public static RepositoryBaseContext ContextFor(DbSession session)
        {
            var user = new Identity(null);
            return new(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        }

        /// <summary>生成随机隔离账号，不使用真实密码材料。</summary>
        private static SysUser NewAccount() => new()
            { UserName = "__M09_" + Guid.NewGuid().ToString("N") + "_", PasswordHash = "专用不可登录哈希" };
    }

    /// <summary>固定夹具审计身份；登录/me 场景可指定专用账号。</summary>
    private sealed class Identity(Guid? id) : ICurrentUser
    {
        public Guid? UserId => id;
        public string UserName => "账号迁移实库夹具";
        public bool IsAuthenticated => id.HasValue;
    }

    /// <summary>注入包装连接，真正的执行仍全部由 Npgsql 完成，不改变事务类型。</summary>
    private sealed class Factory(InterceptConnection connection) : IDbConnectionFactory
    {
        public IDbConnection CreateConnection() => connection;
    }

    /// <summary>在真实命令开始之前插入受控竞争事件，避免用时延猜测分阶段窗口；不改生产仓储。</summary>
    private sealed class InterceptConnection(string connectionString) : DbConnection
    {
        private readonly NpgsqlConnection _inner = new(connectionString);
        public Func<string, Task>? BeforeExecute;
        [AllowNull] public override string ConnectionString { get => _inner.ConnectionString; set => _inner.ConnectionString = value; }
        public override string Database => _inner.Database;
        public override string DataSource => _inner.DataSource;
        public override string ServerVersion => _inner.ServerVersion;
        public override ConnectionState State => _inner.State;
        public override void Open() => _inner.Open();
        public override void Close() => _inner.Close();
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel level) => _inner.BeginTransaction(level);
        protected override DbCommand CreateDbCommand() => new InterceptCommand(this, _inner.CreateCommand());
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }

        /// <summary>转交提供者参数、事务及取消，只在实际执行前调用一次场景钩子。</summary>
        private sealed class InterceptCommand(InterceptConnection connection, NpgsqlCommand inner) : DbCommand
        {
            [AllowNull] public override string CommandText { get => inner.CommandText; set => inner.CommandText = value; }
            public override int CommandTimeout { get => inner.CommandTimeout; set => inner.CommandTimeout = value; }
            public override CommandType CommandType { get => inner.CommandType; set => inner.CommandType = value; }
            public override bool DesignTimeVisible { get => inner.DesignTimeVisible; set => inner.DesignTimeVisible = value; }
            public override UpdateRowSource UpdatedRowSource { get => inner.UpdatedRowSource; set => inner.UpdatedRowSource = value; }
            protected override DbConnection? DbConnection { get => connection; set { } }
            protected override DbTransaction? DbTransaction { get => inner.Transaction; set => inner.Transaction = (NpgsqlTransaction?)value; }
            protected override DbParameterCollection DbParameterCollection => inner.Parameters;
            protected override DbParameter CreateDbParameter() => new NpgsqlParameter();
            public override void Cancel() => inner.Cancel();
            public override void Prepare() => inner.Prepare();
            public override int ExecuteNonQuery() => throw new NotSupportedException();
            public override object? ExecuteScalar() => throw new NotSupportedException();
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
            public override async Task<int> ExecuteNonQueryAsync(CancellationToken token)
            { await BeforeAsync(token); return await inner.ExecuteNonQueryAsync(token); }
            public override async Task<object?> ExecuteScalarAsync(CancellationToken token)
            { await BeforeAsync(token); return await inner.ExecuteScalarAsync(token); }
            protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
            { await BeforeAsync(token); return await inner.ExecuteReaderAsync(behavior, token); }
            /// <summary>取消和钩子等待均有边界，失败立即向场景 finally 传播以执行清理。</summary>
            private async Task BeforeAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (connection.BeforeExecute is { } before)
                    await before(CommandText).WaitAsync(TimeSpan.FromSeconds(15), token);
            }
            protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        }
    }
}
