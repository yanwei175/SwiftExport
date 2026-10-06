using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Application.Services.Auth.Passwords;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Interfaces.Auth.Accounts;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Tools.AdminPasswordRecovery;
using SwiftExport.WebApi.Authentication;
using System.Security.Claims;

namespace SwiftExport.Application.Tests.Database;

/// <summary>仅由主控在固定故障库串行运行；既有管理员角色四字段临时变更后全列核恢复，成功仅保留随机用户和关联。</summary>
[TestClass, DoNotParallelize]
public sealed class PostgreSqlAdminPasswordRecoveryMigrationIntegrationTests
{
    /// <summary>记录随机身份、物理等待与成功保留清单，不记录密码、Hash或连接配置。</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>缺失及所有资格失效分支不写用户；角色显示名/系统标记不能替代角色码授权。</summary>
    [TestMethod, TestCategory("PostgreSqlAdminRecoveryIntegration")]
    [DataRow("missing")]
    [DataRow("ordinary")]
    [DataRow("inactive-user")]
    [DataRow("deleted-user")]
    [DataRow("inactive-role")]
    [DataRow("deleted-role")]
    [DataRow("deleted-link")]
    [DataRow("missing-link")]
    public async Task RealPg_InvalidTargetDoesNotWrite(string mode)
    {
        await using var fixture = await Fixture.Create(TestContext);
        if (mode == "ordinary") await fixture.Connection.ExecuteAsync("UPDATE public.sys_role SET role_code=@Code WHERE uid=@Id", new { Code = fixture.Prefix, Id = fixture.Role.Uid });
        if (mode == "inactive-user") await fixture.Connection.ExecuteAsync("UPDATE public.sys_user SET is_active=FALSE WHERE uid=@Id", new { Id = fixture.User.Uid });
        if (mode == "deleted-user") await fixture.Connection.ExecuteAsync("UPDATE public.sys_user SET is_deleted=TRUE WHERE uid=@Id", new { Id = fixture.User.Uid });
        if (mode == "inactive-role") await fixture.Connection.ExecuteAsync("UPDATE public.sys_role SET is_active=FALSE WHERE uid=@Id", new { Id = fixture.Role.Uid });
        if (mode == "deleted-role") await fixture.Connection.ExecuteAsync("UPDATE public.sys_role SET is_deleted=TRUE WHERE uid=@Id", new { Id = fixture.Role.Uid });
        if (mode == "deleted-link") await fixture.Connection.ExecuteAsync("UPDATE public.sys_user_role SET is_deleted=TRUE WHERE uid=@Id", new { Id = fixture.Link.Uid });
        if (mode == "missing-link") await fixture.Connection.ExecuteAsync("DELETE FROM public.sys_user_role WHERE uid=@Id", new { Id = fixture.Link.Uid });
        var before = await fixture.State();
        var status = await fixture.Store.ResetExistingAdminAsync(mode == "missing" ? fixture.Prefix + "_missing" : fixture.User.UserName, "opaque-input");
        var missing = mode is "missing" or "inactive-user" or "deleted-user";
        Assert.AreEqual(missing ? AdminPasswordRecoveryStatus.TargetNotFound : AdminPasswordRecoveryStatus.TargetNotAdmin, status);
        await fixture.Unchanged(before);
    }

    /// <summary>真实非系统标记管理员按用户名与角色字典匹配，只更新五字段，真实 BCrypt 与 JWT 凭据校验拒绝旧凭据。</summary>
    [TestMethod, TestCategory("PostgreSqlAdminRecoveryIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RealPg_RecoveryCommitsOnlyFiveFieldsAndInvalidatesOldCredential(bool dictionaryUserName)
    {
        await using var fixture = await Fixture.Create(TestContext);
        var before = await fixture.State();
        var unchanged = await fixture.OtherUserFields();
        var roles = await fixture.RoleFacts();
        var earliest = await fixture.Connection.ExecuteScalarAsync<DateTime>("SELECT clock_timestamp()::timestamp");
        var executor = new AdminPasswordRecoveryExecutor(fixture.Store, new BcryptPasswordHasher());
        // 仅随机测试账号；首尾空格用户名经过原Executor规范化，大小写由数据库字典比较。
        var userName = dictionaryUserName ? "  " + fixture.User.UserName.ToUpperInvariant() + "   " : fixture.User.UserName;
        var result = await executor.ExecuteAsync(userName, " recovered-input ", " recovered-input ");
        Assert.IsTrue(result.Success, result.Message);
        fixture.KeepCommitted();
        var after = await fixture.State();
        Assert.AreEqual(before.Version + 1, after.Version);
        Assert.AreEqual(before.CredentialVersion + 1, after.CredentialVersion);
        Assert.AreEqual("LocalAdminPasswordRecovery", after.UpdatedBy);
        Assert.IsTrue(after.UpdatedDate >= earliest);
        Assert.IsTrue(after.UpdatedDate <= await fixture.Connection.ExecuteScalarAsync<DateTime>("SELECT clock_timestamp()::timestamp"));
        var hasher = new BcryptPasswordHasher();
        Assert.IsTrue(hasher.Verify(" recovered-input ", after.Hash));
        Assert.IsFalse(hasher.Verify("initial-input", after.Hash));
        Assert.IsFalse(hasher.Verify("recovered-input", after.Hash));
        Assert.AreEqual(unchanged, await fixture.OtherUserFields());
        Assert.AreEqual(roles, await fixture.RoleFacts());
        Assert.IsFalse(fixture.Role.IsSystem);

        var actor = new TestActor(fixture.User.Uid);
        // 本树未同步另一窗口的PG认证仓储；只读端口直接查询真实PG，不模拟数据库或新增生产依赖。
        var service = new SwiftExport.Application.Services.Auth.Session.AuthSessionService(
            new CredentialReadPort(fixture.Connection), actor, hasher, null!);
        var validator = new CredentialVersionTokenValidator(service);
        ClaimsPrincipal Principal(int credential) => new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, fixture.User.Uid.ToString()),
            new Claim(AuthTokenClaims.CredentialVersion, credential.ToString())], "test"));
        Assert.IsFalse(await validator.ValidateAsync(Principal(before.CredentialVersion)));
        Assert.IsTrue(await validator.ValidateAsync(Principal(after.CredentialVersion)));
        var loggedIn = await service.AuthenticateAsync(new() { UserName = fixture.User.UserName, Password = " recovered-input " });
        Assert.IsTrue(loggedIn.Success);
        Assert.IsFalse((await service.AuthenticateAsync(new() { UserName = fixture.User.UserName, Password = "initial-input" })).Success);
    }

    /// <summary>仓储不裁剪输入；NULL用户名以text绑定并返回缺失，NULL哈希由真实非空约束拒绝且父事务回滚。</summary>
    [TestMethod, TestCategory("PostgreSqlAdminRecoveryIntegration")]
    public async Task RealPg_NullTextAndConstraintFailurePreserveUser()
    {
        await using var fixture = await Fixture.Create(TestContext);
        var before = await fixture.State();
        Assert.AreEqual(AdminPasswordRecoveryStatus.TargetNotFound, await fixture.Store.ResetExistingAdminAsync(null!, "opaque-input"));
        var failure = await Assert.ThrowsAsync<PostgresException>(() => fixture.Store.ResetExistingAdminAsync(fixture.User.UserName, null!));
        Assert.AreEqual("23502", failure.SqlState);
        await fixture.Unchanged(before);
        Assert.AreEqual(AdminPasswordRecoveryStatus.TargetNotFound, await fixture.Store.ResetExistingAdminAsync(" " + fixture.User.UserName, "opaque-input"));
        await fixture.Unchanged(before);
    }

    /// <summary>直接仓储保留尾空格字典匹配；不要求哈希格式，真实成功后随机身份与结果保留供主控核验。</summary>
    [TestMethod, TestCategory("PostgreSqlAdminRecoveryIntegration")]
    public async Task RealPg_RawStoreDoesNotValidateOrTrimHash()
    {
        await using var fixture = await Fixture.Create(TestContext);
        var status = await fixture.Store.ResetExistingAdminAsync(fixture.User.UserName + " ", " opaque-input ");
        Assert.AreEqual(AdminPasswordRecoveryStatus.Updated, status);
        fixture.KeepCommitted();
        Assert.IsTrue((await fixture.State()).Hash == " opaque-input ", "仓储不得清洗或验证输入哈希。");
    }

    /// <summary>等待真实用户行锁后取消，独立后端与服务器等待先成立，再验证没有凭据写入和泄漏事务。</summary>
    [TestMethod, TestCategory("PostgreSqlAdminRecoveryIntegration")]
    public async Task RealPg_CancelWhileServerWaitsDoesNotWrite()
    {
        await using var fixture = await Fixture.Create(TestContext);
        var before = await fixture.State();
        await using var blocker = await fixture.Connection.BeginTransactionAsync(IsolationLevel.Serializable);
        await fixture.Connection.ExecuteAsync("SELECT uid FROM public.sys_user WHERE uid=@Id FOR UPDATE", new { Id = fixture.User.Uid }, blocker);
        using var cancel = new CancellationTokenSource();
        var pending = fixture.Store.ResetExistingAdminAsync(fixture.User.UserName, "opaque-input", cancel.Token);
        try
        {
            await fixture.WaitForStoreBlock(pending);
            cancel.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);
        }
        finally
        {
            cancel.Cancel();
            await blocker.RollbackAsync();
            try { await pending; } catch { /* 已有预期失败仍须等待独立连接释放。 */ }
        }
        await fixture.Unchanged(before);
        await fixture.AssertStoreDisconnected();
    }

    /// <summary>用户竞争保留行锁等待中的旧快照；角色写者先参与目录排队，版本或资格失效均不得被恢复掩盖。</summary>
    [TestMethod, TestCategory("PostgreSqlAdminRecoveryIntegration")]
    [DataRow("version")]
    [DataRow("credential")]
    [DataRow("role")]
    [DataRow("link")]
    [DataRow("user")]
    public async Task RealPg_FinalVersionOrQualificationCompetitionRollsBack(string change)
    {
        await using var fixture = await Fixture.Create(TestContext);
        var before = await fixture.State();
        await using var blocker = await fixture.Connection.BeginTransactionAsync(IsolationLevel.Serializable);
        // 角色变更参与生产角色→用户顺序，避免夹具反向锁用户后再等恢复持有的角色锁。
        if (change == "role")
            await fixture.Connection.ExecuteAsync("LOCK TABLE public.sys_role IN SHARE ROW EXCLUSIVE MODE", transaction: blocker);
        await fixture.Connection.ExecuteAsync("SELECT uid FROM public.sys_user WHERE uid=@Id FOR UPDATE", new { Id = fixture.User.Uid }, blocker);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var pending = fixture.Store.ResetExistingAdminAsync(fixture.User.UserName, "opaque-input", cancel.Token);
        var finished = false;
        try
        {
            await fixture.WaitForStoreBlock(pending, beforeSnapshot: change == "role");
            var sql = change switch
            {
                "version" => "UPDATE public.sys_user SET version=version+1 WHERE uid=@Id",
                "credential" => "UPDATE public.sys_user SET credential_version=credential_version+1 WHERE uid=@Id",
                "user" => "UPDATE public.sys_user SET is_active=FALSE WHERE uid=@Id",
                "role" => "UPDATE public.sys_role SET is_active=FALSE WHERE uid=@Id",
                _ => "UPDATE public.sys_user_role SET is_deleted=TRUE WHERE uid=@Id"
            };
            var id = change == "role" ? fixture.Role.Uid : change == "link" ? fixture.Link.Uid : fixture.User.Uid;
            await fixture.Connection.ExecuteAsync(sql, new { Id = id }, blocker);
            await blocker.CommitAsync();
            finished = true;
            try
            {
                var status = await pending;
                Assert.AreNotEqual(AdminPasswordRecoveryStatus.Updated, status, "已提交的竞争资格/版本变化不能被恢复写入掩盖。");
            }
            catch (PostgresException error)
            {
                Assert.AreEqual("40001", error.SqlState, "串行化失败原样传播，不重试业务。");
            }
        }
        finally
        {
            cancel.Cancel();
            if (!finished) await blocker.RollbackAsync();
            try { await pending; } catch { /* 观察之后等连接结束，清理只针对本例随机身份。 */ }
        }
        var after = await fixture.State();
        Assert.IsTrue(after.Hash == before.Hash, "恢复失败不得改变凭据。");
        Assert.AreEqual(before.UpdatedBy, after.UpdatedBy);
        Assert.AreEqual(before.UpdatedDate, after.UpdatedDate);
        Assert.AreEqual(before.Version + (change == "version" ? 1 : 0), after.Version);
        Assert.AreEqual(before.CredentialVersion + (change == "credential" ? 1 : 0), after.CredentialVersion);
        await fixture.AssertStoreDisconnected();
    }

    /// <summary>固定故障库原生连接；保存单一既有管理员角色完整快照，仅新增随机用户和关联，角色无增量。</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly string ConnectionString;
        internal readonly NpgsqlConnection Connection;
        internal readonly NpgsqlConnection Monitor;
        internal readonly SysUser User;
        internal SysRole Role { get; private set; } = null!;
        internal readonly SysUserRole Link;
        internal readonly string Prefix = "p06_ar_" + Guid.NewGuid().ToString("N");
        internal readonly string ApplicationName = "p06_ar_store_" + Guid.NewGuid().ToString("N");
        internal readonly IAdminPasswordRecoveryStore Store;
        private readonly TestContext _output;
        private bool _keep;
        private int _ownerPid;
        private string? _roleSnapshot;
        private Guid? _roleUid;
        private readonly List<Task<AdminPasswordRecoveryStatus>> _storeTasks = [];
        private bool _disposed;
        private bool _roleMutationAttempted;
        private bool _roleRestored;
        private static readonly EntityMetadata RoleMetadata = PostgreSqlEntityMetadata.GetMetadata(typeof(SysRole));
        private static readonly string RoleColumns = string.Join(",", RoleMetadata.Columns.Select(x =>
            PostgreSqlNames.Quote(x.ColumnName) + " AS " + PostgreSqlNames.Quote(x.PropertyName)));

        /// <summary>分配两个随机身份及独立连接名；既有角色必须在创建前读取保存，构造器不访问数据库。</summary>
        private Fixture(string connectionString, TestContext output)
        {
            ConnectionString = connectionString; _output = output;
            Connection = new NpgsqlConnection(connectionString);
            Monitor = new NpgsqlConnection(connectionString);
            var settings = new NpgsqlConnectionStringBuilder(connectionString) { ApplicationName = ApplicationName, Pooling = false };
            Store = new TrackedStore(this, new PostgreSqlAdminPasswordRecoveryStore(settings.ConnectionString));
            User = new SysUser { Uid = Guid.NewGuid(), UserName = Prefix, NickName = "恢复测试", PasswordHash = new BcryptPasswordHasher().Hash("initial-input"),
                Version = 3, CredentialVersion = 2, Email = Prefix + "@invalid.test", PhoneNumber = "test-only" };
            Link = new SysUserRole { Uid = Guid.NewGuid(), UserId = User.Uid };
        }

        /// <summary>先强门禁并保存单一规范管理员角色全部列，再原子提交两条新行及角色临时资格；创建失败也必须恢复。</summary>
        internal static async Task<Fixture> Create(TestContext output)
        {
            var connectionString = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
            var settings = new NpgsqlConnectionStringBuilder(connectionString);
            Assert.IsFalse(settings.Multiplexing);
            Assert.IsFalse(settings.NoResetOnClose);
            var fixture = new Fixture(connectionString, output);
            try
            {
                await fixture.Connection.OpenAsync();
                await fixture.Monitor.OpenAsync();
                fixture._ownerPid = await fixture.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()");
                Assert.AreEqual("Asia/Shanghai", await fixture.Connection.ExecuteScalarAsync<string>("SHOW TimeZone"), "本地时刻环境与主控冻结一致。");
                await fixture.CaptureRole();
                await using var transaction = await fixture.Connection.BeginTransactionAsync();
                await fixture.Add(fixture.User, transaction);
                // 同一角色的合法字典别名验证大小写/尾空格；不创建第二管理员身份，不推进版本或审计。
                fixture.Role.RoleCode = "system_admin "; fixture.Role.IsSystem = false;
                fixture._roleMutationAttempted = true;
                Assert.AreEqual(1, await fixture.Connection.ExecuteAsync("""
                    UPDATE public.sys_role SET role_code=@RoleCode,is_system=@IsSystem,
                        is_active=@IsActive,is_deleted=@IsDeleted WHERE uid=@Uid
                    """, new { fixture.Role.RoleCode, fixture.Role.IsSystem, fixture.Role.IsActive,
                        fixture.Role.IsDeleted, fixture.Role.Uid }, transaction));
                await fixture.Add(fixture.Link, transaction);
                await transaction.CommitAsync();
                fixture.Manifest("两条新行及既有角色临时资格已提交", false);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        /// <summary>预读唯一字典管理员并一次保存全部物理列；快照取得前不得修改角色或新增夹具行。</summary>
        private async Task CaptureRole()
        {
            var captures = (await Connection.QueryAsync<RoleCapture>("""
                SELECT r.uid AS "Uid",to_jsonb(r)::text AS "Snapshot"
                FROM public.sys_role r
                WHERE rtrim(r.role_code,' ') COLLATE public.erp_dictionary_ci
                    = 'SYSTEM_ADMIN' COLLATE public.erp_dictionary_ci
                """)).ToArray();
            Assert.HasCount(1, captures, "测试库必须恰有一个既有管理员身份，不能借目标raw唯一缺口新增别名。");
            var capture = captures.Single();
            // 先保存精确UID和原始全列JSON，后续读取或初始化失败也有恢复依据。
            _roleUid = capture.Uid; _roleSnapshot = capture.Snapshot;
            Assert.IsNotNull(_roleSnapshot);
            Role = await Connection.QuerySingleAsync<SysRole>($"SELECT {RoleColumns} FROM public.sys_role WHERE uid=@Id", new { Id = capture.Uid });
            Assert.AreEqual("SYSTEM_ADMIN", Role.RoleCode, "开始前要求规范角色码，避免覆盖其他测试遗留别名。");
            Assert.IsTrue(Role.IsSystem); Assert.IsTrue(Role.IsActive); Assert.IsFalse(Role.IsDeleted);
            Link.RoleId = Role.Uid;
            Manifest("既有角色全列快照已保存，尚未修改", false);
        }

        /// <summary>全列原始JSON含未知物理列，恢复验证不局限于实体当前属性集。</summary>
        private sealed class RoleCapture
        {
            public Guid Uid { get; set; }
            public string Snapshot { get; set; } = null!;
        }

        /// <summary>用共享元数据和参数类型插入合法随机夹具，不删除约束或复制实体映射。</summary>
        private Task<int> Add(BaseEntity entity, NpgsqlTransaction transaction)
        {
            entity.CreatedDate = entity.UpdatedDate = new DateTime(2026, 10, 5, 9, 0, 0);
            entity.CreatedBy = entity.UpdatedBy = Prefix;
            var metadata = PostgreSqlEntityMetadata.GetMetadata(entity.GetType());
            var columns = string.Join(",", metadata.Columns.Select(x => PostgreSqlNames.Quote(x.ColumnName)));
            var values = string.Join(",", metadata.Columns.Select(x => "@" + x.ParameterName));
            return Connection.ExecuteAsync($"INSERT INTO public.{PostgreSqlNames.Quote(metadata.TableName)} ({columns}) VALUES ({values})",
                PostgreSqlParameters.Entity(entity, metadata), transaction);
        }

        /// <summary>只读取本例用户凭据状态；Hash只用于布尔比较，不写测试输出。</summary>
        internal Task<UserState> State() => Connection.QuerySingleAsync<UserState>("""
            SELECT version AS "Version",credential_version AS "CredentialVersion",password_hash AS "Hash",
                   updated_date AS "UpdatedDate",updated_by AS "UpdatedBy"
            FROM public.sys_user WHERE uid=@Id
            """, new { Id = User.Uid });

        /// <summary>比较所有非五更新字段，JSON明确排除Hash，失败输出不含凭据。</summary>
        internal Task<string?> OtherUserFields() => Connection.ExecuteScalarAsync<string>("""
            SELECT (to_jsonb(u)-ARRAY['password_hash','credential_version','version','updated_date','updated_by'])::text
            FROM public.sys_user u WHERE uid=@Id
            """, new { Id = User.Uid });

        /// <summary>比较被暂改的既有共享角色及本例新关联完整JSON；Store前后不变断言仍保留，原始快照另在finally验证。</summary>
        internal Task<string?> RoleFacts() => Connection.ExecuteScalarAsync<string>("""
            SELECT jsonb_build_object('role',(SELECT to_jsonb(r) FROM public.sys_role r WHERE uid=@Role),
                'link',(SELECT to_jsonb(l) FROM public.sys_user_role l WHERE uid=@Link))::text
            """, new { Role = Role.Uid, Link = Link.Uid });

        /// <summary>拒绝/取消后凭据、双版本和审计均必须相同。</summary>
        internal async Task Unchanged(UserState before)
        {
            var after = await State();
            Assert.IsTrue(after.Hash == before.Hash, "拒绝分支不得改凭据。");
            Assert.AreEqual(before.Version, after.Version);
            Assert.AreEqual(before.CredentialVersion, after.CredentialVersion);
            Assert.AreEqual(before.UpdatedDate, after.UpdatedDate);
            Assert.AreEqual(before.UpdatedBy, after.UpdatedBy);
        }

        /// <summary>区分首角色锁与用户行锁快照，使用两个独立backend和服务器等待同步。</summary>
        internal async Task WaitForStoreBlock(Task pending, bool beforeSnapshot = false)
        {
            var owner = _ownerPid;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!timeout.IsCancellationRequested)
            {
                var pid = await Monitor.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
                    SELECT pid FROM pg_stat_activity WHERE application_name=@Name AND state='active'
                      AND wait_event_type='Lock' AND @Owner=ANY(pg_blocking_pids(pid))
                      AND ((@BeforeSnapshot AND query ILIKE 'LOCK TABLE%SHARE ROW EXCLUSIVE%')
                        OR (NOT @BeforeSnapshot AND backend_xmin IS NOT NULL))
                    """, new { Name = ApplicationName, Owner = owner, BeforeSnapshot = beforeSnapshot }, cancellationToken: timeout.Token));
                if (pid.HasValue)
                {
                    Assert.AreNotEqual(owner, pid.Value);
                    _output.WriteLine($"真实锁等待：owner={owner},store={pid.Value},用户={User.Uid}");
                    return;
                }
                if (pending.IsCompleted) { await pending; Assert.Fail("恢复在预期服务器等待之前结束。"); }
                await Task.Delay(20, timeout.Token);
            }
            Assert.Fail("未观察到恢复独立后端的行锁等待。");
        }

        /// <summary>恢复连接使用Pooling=false；结束后该独立应用后端必须释放。</summary>
        internal async Task AssertStoreDisconnected() => Assert.AreEqual(0,
            await Monitor.ExecuteScalarAsync<int>("SELECT COUNT(*)::int FROM pg_stat_activity WHERE application_name=@Name", new { Name = ApplicationName }));

        /// <summary>Store真实提交后立即保留随机用户/关联，后续断言失败也登记；既有角色仍无条件恢复。</summary>
        internal void KeepCommitted()
        {
            if (_keep) return;
            _keep = true; Manifest("恢复真实提交，新User/Link保留，既有Role待恢复", true);
        }

        /// <summary>只登记生命周期和精确身份；角色明确是既有共享记录，不输出密码、Hash或连接。</summary>
        private void Manifest(string phase, bool keep) => _output.WriteLine(JsonSerializer.Serialize(new
        {
            Phase = phase, User = User.Uid, Link = Link.Uid, ExistingSharedRole = _roleUid,
            RoleSnapshotSaved = _roleSnapshot is not null, NewRoleCount = 0,
            RoleRecordOrigin = "既有共享记录", RoleMutationAttempted = _roleMutationAttempted,
            ExistingRoleRestoredAndFullSnapshotVerified = _roleRestored,
            RoleMutationFields = new[] { "RoleCode", "IsSystem", "IsActive", "IsDeleted" },
            KeepNewUserAndLink = keep, RolePolicy = "既有角色仅暂改四字段，finally精确恢复并校验全部列"
        }));

        /// <summary>只观察真实Store任务和成功保留边界；输入、token、返回、失败和物理连接全部由原Store处理。</summary>
        private sealed class TrackedStore(Fixture owner, PostgreSqlAdminPasswordRecoveryStore inner) : IAdminPasswordRecoveryStore
        {
            /// <summary>跟踪真正委托的终结；Updated到达时先记保留，测试断言仍使用原状态而不包装异常。</summary>
            public Task<AdminPasswordRecoveryStatus> ResetExistingAdminAsync(string userName, string passwordHash,
                CancellationToken cancellationToken = default)
            {
                var task = Observe(inner.ResetExistingAdminAsync(userName, passwordHash, cancellationToken));
                owner._storeTasks.Add(task);
                return task;
            }
            /// <summary>只在生产Store已提交后登记身份，不改写状态、不重试业务。</summary>
            private async Task<AdminPasswordRecoveryStatus> Observe(Task<AdminPasswordRecoveryStatus> task)
            {
                var result = await task;
                if (result == AdminPasswordRecoveryStatus.Updated) owner.KeepCommitted();
                return result;
            }
        }

        /// <summary>先关闭owner连接结束其全部事务/行锁，再等待Store任务终结；预期取消/失败由原用例断言。</summary>
        private async Task EndOwnerAndWaitStore()
        {
            try { await Connection.CloseAsync(); }
            finally
            {
                foreach (var task in _storeTasks)
                    try { await task; }
                    catch { /* 原18用例保留状态/异常断言；这里只等待物理Store完成，恢复失败绝不在此吞掉。 */ }
            }
        }

        /// <summary>用独立同门禁连接精确恢复角色四字段，全列JSON相等才通过；不加版本/audit，也不删既有角色。</summary>
        private async Task RestoreRoleAndCleanNewRows()
        {
            // 角色快照之前绝无DB写入，创建失败时不能凭猜测UID恢复或清理旧角色。
            if (_roleSnapshot is null || !_roleUid.HasValue) return;
            await using var cleanup = new NpgsqlConnection(PostgreSqlIntegrationEnvironment.GetFailureConnectionString());
            await cleanup.OpenAsync();
            Assert.AreEqual(1, await cleanup.ExecuteAsync(new CommandDefinition("""
                UPDATE public.sys_role r SET role_code=saved.role_code,is_system=saved.is_system,
                    is_active=saved.is_active,is_deleted=saved.is_deleted
                FROM jsonb_populate_record(NULL::public.sys_role,CAST(@Snapshot AS jsonb)) saved
                WHERE r.uid=@Id
                """, new { Snapshot = _roleSnapshot, Id = _roleUid.Value }, commandTimeout: 10)),
                "必须恢复精确既有角色，缺行不能作为清理成功。");
            var restored = await cleanup.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT to_jsonb(r)::text FROM public.sys_role r WHERE uid=@Id",
                new { Id = _roleUid.Value }, commandTimeout: 10));
            Assert.IsTrue(_roleSnapshot == restored, $"既有角色全部列必须恢复原快照：{_roleUid.Value}。");
            _roleRestored = true;
            Manifest("既有Role四字段恢复且全部列核对通过", _keep);
            if (_keep) return;
            await using var transaction = await cleanup.BeginTransactionAsync();
            await cleanup.ExecuteAsync("DELETE FROM public.sys_user_role WHERE uid=@Id AND user_id=@User AND role_id=@Role",
                new { Id = Link.Uid, User = User.Uid, Role = _roleUid.Value }, transaction);
            await cleanup.ExecuteAsync("DELETE FROM public.sys_user WHERE uid=@Id AND user_name=@Name",
                new { Id = User.Uid, Name = User.UserName }, transaction);
            await transaction.CommitAsync();
            Manifest("失败/拒绝，仅精确清理新User/Link，既有Role已恢复", false);
        }

        /// <summary>无条件结束owner/Store并恢复既有共享角色；成功仅保留两条新行，恢复失败传播而非假称通过。</summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                try { await EndOwnerAndWaitStore(); }
                finally
                {
                    try { await RestoreRoleAndCleanNewRows(); }
                    catch { Manifest("角色恢复或新行清理失败，不得标通过", _keep); throw; }
                }
            }
            finally
            {
                try { await Monitor.DisposeAsync(); }
                finally { await Connection.DisposeAsync(); }
            }
        }
    }

    /// <summary>接收五更新字段结果，凭据不进入测试日志。</summary>
    private sealed class UserState
    {
        public int Version { get; set; }
        public int CredentialVersion { get; set; }
        public string Hash { get; set; } = "";
        public DateTime? UpdatedDate { get; set; }
        public string? UpdatedBy { get; set; }
    }


    /// <summary>仅给真实登录和JWT版本服务提供真实PG读取；不替代未同步的生产认证仓储或授予写权限。</summary>
    private sealed class CredentialReadPort(NpgsqlConnection connection) : IAuthRepository
    {
        private static readonly EntityMetadata Metadata = PostgreSqlEntityMetadata.GetMetadata(typeof(SysUser));
        private static readonly string Columns = string.Join(",", Metadata.Columns.Select(x =>
            PostgreSqlNames.Quote(x.ColumnName) + " AS " + PostgreSqlNames.Quote(x.PropertyName)));

        /// <summary>直接读真实活动账号及凭据供实际BCrypt登录验证，保留字典用户名比较。</summary>
        public Task<SysUser?> GetActiveUserByNameAsync(string userName, CancellationToken cancellationToken = default) =>
            connection.QuerySingleOrDefaultAsync<SysUser>(new CommandDefinition(
                $"SELECT {Columns} FROM public.sys_user WHERE rtrim(user_name,' ') COLLATE public.erp_dictionary_ci=rtrim(@UserName,' ') COLLATE public.erp_dictionary_ci AND is_active=TRUE AND COALESCE(is_deleted,FALSE)=FALSE",
                new { UserName = userName }, cancellationToken: cancellationToken));

        /// <summary>按身份读真实活动账号，不制造凭据投影。</summary>
        public Task<SysUser?> GetActiveUserByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            connection.QuerySingleOrDefaultAsync<SysUser>(new CommandDefinition(
                $"SELECT {Columns} FROM public.sys_user WHERE uid=@Id AND is_active=TRUE AND COALESCE(is_deleted,FALSE)=FALSE",
                new { Id = userId }, cancellationToken: cancellationToken));

        /// <summary>每次直接查真实账号当前凭据版本，不缓存或模拟旧JWT失效。</summary>
        public Task<bool> IsCredentialVersionValidAsync(Guid userId, int credentialVersion, CancellationToken cancellationToken = default) =>
            connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS(SELECT 1 FROM public.sys_user WHERE uid=@Id AND credential_version=@Version AND is_active=TRUE AND COALESCE(is_deleted,FALSE)=FALSE)",
                new { Id = userId, Version = credentialVersion }, cancellationToken: cancellationToken));

        /// <summary>本窄测试端口不参与管理员授权，意外调用立即失败。</summary>
        public Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <summary>本例不提供用户名创建资格检查。</summary>
        public Task<bool> IsUserNameInUseAsync(string userName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <summary>本例不能通过认证端口创建账号。</summary>
        public Task<AuthUserCreateStatus> CreateUserByAdminAsync(Guid actorUserId, SysUser user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <summary>恢复只能经被验真实Store写入，本端口禁止改密。</summary>
        public Task<bool> UpdateOwnPasswordAsync(SysUser user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <summary>本例不能绕过被验Store重置用户。</summary>
        public Task<AdminPasswordResetStatus> ResetNonAdminPasswordAsync(Guid actorUserId, SysUser targetUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>只提供真实随机用户身份给只读凭据服务，不授予额外权限。</summary>
    private sealed class TestActor(Guid uid) : ICurrentUser
    {
        /// <summary>夹具身份已认证。</summary>
        public bool IsAuthenticated => true;
        /// <summary>读取本例用户身份。</summary>
        public Guid? UserId => uid;
        /// <summary>仅为读取上下文提供固定审计名。</summary>
        public string UserName => "P06RecoveryTest";
    }
}

