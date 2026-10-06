using System.Data;
using System.Diagnostics;
using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Auth.Permissions;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>主控串行验收用 RBAC 仓储场景；默认 Serializable 回滚，提交竞争只清理随机夹具，不写既有管理员或权限。</summary>
[TestClass]
[DoNotParallelize]
[TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlRbacManagementMigrationIntegrationTests
{
    /// <summary>将真实查询计划与耗时写入主控验收 TRX，不输出连接配置。</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>完整覆盖用户/角色/权限查询和计数；用户DTO无凭据，活动集合和目录可分配性按原边界读取。</summary>
    [TestMethod]
    public async Task QueriesAndLocks_MapAllReadContracts()
    {
        using var f = new Fixture();
        await f.SeedAsync();
        Assert.IsTrue(await f.Repository.LockAndCheckAdministratorAsync(f.Actor.Uid, default));
        Assert.IsFalse(await f.Repository.LockAndCheckAdministratorAsync(f.Target.Uid, default));
        var user = await f.Repository.GetUserAsync(f.Target.Uid, default);
        Assert.IsNotNull(user);
        Assert.AreEqual(f.Target.UserName, user.UserName);
        Assert.IsNull(user.Email);
        Assert.IsFalse(user.IsAdmin);
        Assert.IsNotNull(await f.Repository.GetUserForUpdateAsync(f.Target.Uid, default));
        var role = await f.Repository.GetRoleAsync(f.Role.Uid, default);
        Assert.IsNotNull(role);
        Assert.AreEqual(f.Role.RoleCode, role.RoleCode);
        Assert.IsNotNull(await f.Repository.GetRoleForUpdateAsync(f.Role.Uid, default));
        Assert.AreEqual(f.Role.Uid, (await f.Repository.GetActiveRolesAsync([f.Role.Uid], default)).Single().Uid);
        Assert.AreEqual(f.AdminRoleId, (await f.Repository.GetUserRoleIdsAsync(f.Actor.Uid, default)).Single());
        Assert.AreEqual(f.Permission.Uid, (await f.Repository.GetRolePermissionIdsAsync(f.Role.Uid, default)).Single());
        Assert.IsTrue((await f.Repository.GetPermissionsAsync(default)).Any(p => p.Uid == f.Permission.Uid && !p.IsAssignable));
        Assert.IsFalse((await f.Repository.GetActivePermissionsAsync([f.Permission.Uid], default)).Single().IsAssignable);
        Assert.IsTrue(await f.Repository.CountActiveAdministratorsAsync(default) >= 1);
        Assert.AreEqual(0, await f.Repository.CountRoleUsersAsync(f.Role.Uid, default));
        var users = await f.Repository.SearchUsersAsync(new ManagedPageRequest { Keyword = f.Target.UserName.ToLowerInvariant() }, default);
        Assert.AreEqual(1, users.TotalCount);
        Assert.AreEqual(f.Target.Uid, users.Items.Single().Uid);
        var roles = await f.Repository.SearchRolesAsync(new ManagedPageRequest { Keyword = f.Role.RoleCode.ToLowerInvariant() }, default);
        Assert.AreEqual(1, roles.TotalCount);
        Assert.AreEqual(f.Role.Uid, roles.Items.Single().Uid);
    }

    /// <summary>资料和状态分支保留原空值语义、版本与凭据独立性，普通角色软删后不再返回。</summary>
    [TestMethod]
    public async Task UpdatesAndCreate_PreserveVersionsAndBranches()
    {
        using var f = new Fixture();
        await f.SeedAsync();
        Assert.IsTrue(await f.Repository.LockAndCheckAdministratorAsync(f.Actor.Uid, default));
        Assert.IsTrue(await f.Repository.UpdateUserAsync(f.Target.Uid, 1, "新昵称", null, null, null, default));
        Assert.IsFalse(await f.Repository.UpdateUserAsync(f.Target.Uid, 1, "旧", null, null, null, default));
        Assert.IsTrue(await f.Repository.UpdateUserAsync(f.Target.Uid, 2, null, null, null, false, default));
        var user = await f.Repository.GetUserAsync(f.Target.Uid, default);
        Assert.IsNotNull(user);
        Assert.AreEqual("新昵称", user.NickName);
        Assert.AreEqual(3, user.Version);
        Assert.IsFalse(user.IsActive);
        Assert.AreEqual(1, await f.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT credential_version FROM public.sys_user WHERE uid = @Uid;", new { f.Target.Uid }, f.Session.Transaction)));
        var id = await f.Repository.CreateRoleAsync(f.Prefix + "CREATED", "普通", null, default);
        var created = await f.Repository.GetRoleAsync(id, default);
        Assert.IsNotNull(created);
        Assert.IsFalse(created.IsSystem); Assert.IsTrue(created.IsActive); Assert.AreEqual(1, created.Version);
        Assert.IsTrue(await f.Repository.UpdateRoleAsync(f.Role.Uid, 1, "新角色", null, null, false, default));
        Assert.IsTrue(await f.Repository.UpdateRoleAsync(f.Role.Uid, 2, null, "应忽略", false, false, default));
        var role = await f.Repository.GetRoleAsync(f.Role.Uid, default);
        Assert.IsNotNull(role); Assert.AreEqual("新角色", role.RoleName); Assert.IsNull(role.Description); Assert.IsFalse(role.IsActive);
        Assert.IsFalse(await f.Repository.UpdateRoleAsync(f.AdminRoleId, 1, "禁止", null, false, true, default));
        Assert.IsTrue(await f.Repository.UpdateRoleAsync(f.Role.Uid, 3, null, null, null, true, default));
        Assert.IsNull(await f.Repository.GetRoleAsync(f.Role.Uid, default));
    }

    /// <summary>整组替换后清空集合，聚合版本与旧关系软删除版本递增；停用用户仍计入角色关系数。</summary>
    [TestMethod]
    public async Task ReplacementAndClear_KeepHistoricalRelations()
    {
        using var f = new Fixture();
        await f.SeedAsync();
        Assert.IsTrue(await f.Repository.LockAndCheckAdministratorAsync(f.Actor.Uid, default));
        await f.Repository.GetUserForUpdateAsync(f.Target.Uid, default);
        await f.Repository.ReplaceUserRolesAsync(f.Target.Uid, 1, [f.Role.Uid], default);
        Assert.AreEqual(1, await f.Repository.CountRoleUsersAsync(f.Role.Uid, default));
        Assert.IsTrue(await f.Repository.UpdateUserAsync(f.Target.Uid, 2, null, null, null, false, default));
        Assert.AreEqual(1, await f.Repository.CountRoleUsersAsync(f.Role.Uid, default));
        await f.Repository.ReplaceUserRolesAsync(f.Target.Uid, 3, [], default);
        Assert.HasCount(0, await f.Repository.GetUserRoleIdsAsync(f.Target.Uid, default));
        Assert.AreEqual(0, await f.Repository.CountRoleUsersAsync(f.Role.Uid, default));
        Assert.AreEqual(4, (await f.Repository.GetUserAsync(f.Target.Uid, default))!.Version);
        await f.Repository.GetRoleForUpdateAsync(f.Role.Uid, default);
        await f.Repository.ReplaceRolePermissionsAsync(f.Role.Uid, 1, [f.Permission.Uid], default);
        Assert.AreEqual(f.Permission.Uid, (await f.Repository.GetRolePermissionIdsAsync(f.Role.Uid, default)).Single());
        await f.Repository.ReplaceRolePermissionsAsync(f.Role.Uid, 2, [], default);
        Assert.HasCount(0, await f.Repository.GetRolePermissionIdsAsync(f.Role.Uid, default));
        Assert.AreEqual(3, (await f.Repository.GetRoleAsync(f.Role.Uid, default))!.Version);
    }

    /// <summary>替换中途重复关系触发唯一失败，显式回滚后由另一连接确认版本、关系都未提交。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ReplacementFailure_RollsBackAggregateAndRelations(bool users)
    {
        using var f = new Fixture(false);
        try
        {
            await f.SeedAsync();
            f.BeginSerializable();
            Assert.IsTrue(await f.Repository.LockAndCheckAdministratorAsync(f.Actor.Uid, default));
            var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => users
                ? f.Repository.ReplaceUserRolesAsync(f.Target.Uid, 1, [f.Role.Uid, f.Role.Uid], default)
                : f.Repository.ReplaceRolePermissionsAsync(f.Role.Uid, 1, [f.Permission.Uid, f.Permission.Uid], default));
            Assert.AreEqual("23505", error.SqlState);
            f.End(false);
            using var observer = f.NewSession();
            var repo = new PostgreSqlRbacManagementRepository(ContextFor(observer));
            Assert.AreEqual(1, (await repo.GetUserAsync(f.Target.Uid, default))!.Version);
            Assert.AreEqual(1, (await repo.GetRoleAsync(f.Role.Uid, default))!.Version);
            Assert.HasCount(0, await repo.GetUserRoleIdsAsync(f.Target.Uid, default));
            Assert.AreEqual(f.Permission.Uid, (await repo.GetRolePermissionIdsAsync(f.Role.Uid, default)).Single());
        }
        finally { f.End(false); await f.CleanupAsync(); }
    }

    /// <summary>保留原 SQLServer 字符组断言，补齐后应通过；不将旧失败改写成字面匹配。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlLikeCompatibilityGap")]
    public async Task SearchBracketClass_ExposesSqlServerContractGap()
    {
        using var f = new Fixture();
        f.Target.NickName = f.Prefix + "Alpha";
        await f.SeedAsync();
        var page = await f.Repository.SearchUsersAsync(new ManagedPageRequest { Keyword = f.Prefix + "[Aa]lpha" }, default);
        Assert.IsTrue(page.Items.Any(user => user.Uid == f.Target.Uid),
            "SQL Server LIKE [Aa] 字符组应匹配 Alpha；PG 原LIKE未支持此语义，此断言故意保留原查询契约。");
    }

    /// <summary>只在随机夹具昵称/角色名内对照源库常量样例；每个样例同时断言 count 和分页，不改既有数据。</summary>
    [TestMethod]
    [DataRow("a", "[ab]", true)]
    [DataRow("d", "[^abc]", true)]
    [DataRow("a", "[^abc]", false)]
    [DataRow("A", "[a-z]", true)]
    [DataRow("é", "[a-z]", true)]
    [DataRow("é", "[e]", false)]
    [DataRow("É", "[é]", true)]
    [DataRow("b", "[a-c]", true)]
    [DataRow("b", "[c-a]", false)]
    [DataRow("b", "[z-ab]", true)]
    [DataRow("b", "[^z-a]", true)]
    [DataRow("-", "[a-]", true)]
    [DataRow("-", "[-a]", true)]
    [DataRow("-", "[a-b-c]", false)]
    [DataRow("c", "[a-b-c]", true)]
    [DataRow("d", "[a-c-e]", false)]
    [DataRow("e", "[a-c-e]", true)]
    [DataRow("d", "[a-b-c-e]", true)]
    [DataRow("c", "[a-b--c]", false)]
    [DataRow("-", "[a-b---]", false)]
    [DataRow("-", "[a-b--]", true)]
    [DataRow("c", "[a-b--cd]", false)]
    [DataRow("d", "[a-b--cd]", true)]
    [DataRow("c", "[a-b---cd]", true)]
    [DataRow("d", "[a-b--c-e]", false)]
    [DataRow("e", "[a-b--c-e]", true)]
    [DataRow("^", "[a^]", true)]
    [DataRow("]", "[a]]", false)]
    [DataRow("]", "[]]", false)]
    [DataRow("[", "[[]", true)]
    [DataRow("a", "[", false)]
    [DataRow("[", "[", false)]
    [DataRow("a", "[]", false)]
    [DataRow("a", "[^]", true)]
    [DataRow(" ", "[^]", true)]
    [DataRow("%", "[^]", true)]
    [DataRow("]", "]", true)]
    [DataRow("Ab\\xy", "[a]%\\_[y]", true)]
    [DataRow("AB", "[a][^c]", true)]
    [DataRow("AC", "[a][^c]", false)]
    [DataRow("abacadaeaf", "[a][b]%[c]%[d]%[e]%[f]", true)]
    public async Task SearchCharacterGroups_PreserveSourceSamples(string value, string pattern, bool expected)
    {
        using var f = new Fixture();
        f.Target.NickName = f.Prefix + value + "_END";
        f.Role.RoleName = f.Target.NickName;
        await f.SeedAsync();
        var request = new ManagedPageRequest { Keyword = f.Prefix + pattern + "_END", PageSize = 1 };
        var users = await f.Repository.SearchUsersAsync(request, default);
        var roles = await f.Repository.SearchRolesAsync(request, default);
        Assert.AreEqual(expected ? 1 : 0, users.TotalCount, "用户 count：" + pattern);
        Assert.AreEqual(expected ? 1 : 0, roles.TotalCount, "角色 count：" + pattern);
        Assert.AreEqual(expected, users.Items.Any(u => u.Uid == f.Target.Uid));
        Assert.AreEqual(expected, roles.Items.Any(r => r.Uid == f.Role.Uid));
        request.PageIndex = 2;
        var second = await f.Repository.SearchUsersAsync(request, default);
        Assert.AreEqual(users.TotalCount, second.TotalCount);
        Assert.HasCount(0, second.Items);
    }

    /// <summary>保留源库复杂标点范围/连续连字符的真值，冻结 ICU 不保证相同排序；不得删失败用例宣称全兼容。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlLikeCollationBoundary")]
    [DataRow("z", "[a--c]", true)]
    [DataRow("z", "[a---]", true)]
    public Task SearchPunctuationRanges_KeepSourceExpectations(string value, string pattern, bool expected) =>
        SearchCharacterGroups_PreserveSourceSamples(value, pattern, expected);

    /// <summary>默认昵称 NULL 不匹配字符组；用户名仍可匹配，空关键词保留原未筛选行为。</summary>
    [TestMethod]
    public async Task SearchCharacterGroups_NullAndEmptyKeepOriginalBoundary()
    {
        using var f = new Fixture();
        await f.SeedAsync();
        var missing = await f.Repository.SearchUsersAsync(new ManagedPageRequest { Keyword = f.Prefix + "[Z]_END" }, default);
        Assert.AreEqual(0, missing.TotalCount);
        var name = await f.Repository.SearchUsersAsync(new ManagedPageRequest { Keyword = f.Prefix + "[T]ARGET" }, default);
        Assert.AreEqual(f.Target.Uid, name.Items.Single().Uid);
        var empty = await f.Repository.SearchUsersAsync(new ManagedPageRequest { Keyword = "  " }, default);
        Assert.IsTrue(empty.TotalCount >= 2);
    }

    /// <summary>用仓储实际谓词、参数和投影记录 count/page 的真实计划；十五秒命令上限防止验收无限等待。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SearchPlans_RecordFastPathAndMultipleGroups(bool bracket)
    {
        using var f = new Fixture();
        f.Target.NickName = f.Prefix + new string('a', 60) + "bcdef_END";
        await f.SeedAsync();
        var request = new ManagedPageRequest { Keyword = f.Prefix + (bracket ? "[a]%[b]%[c]%[d]%[e]%[f]_END" : "abcdef_END") };
        var type = typeof(PostgreSqlRbacManagementRepository);
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        var filter = (string)type.GetMethod("PageFilter", flags)!.Invoke(null, ["u", "user_name", "nick_name", bracket])!;
        var parameters = type.GetMethod("PageParameters", flags)!.Invoke(null, [request]);
        var columns = (string)type.GetField("UserColumns", flags)!.GetValue(null)!;
        foreach (var sql in new[] { $"SELECT COUNT(*) FROM public.sys_user u WHERE {filter};",
            $"SELECT {columns} FROM public.sys_user u WHERE {filter} ORDER BY rtrim(u.user_name, ' ') COLLATE public.erp_dictionary_ci, u.uid LIMIT @PageSize OFFSET @Offset;" })
        {
            var watch = Stopwatch.StartNew();
            var plan = await f.Session.Connection.QueryAsync<string>(new CommandDefinition(
                "EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT) " + sql, parameters, f.Session.Transaction, commandTimeout: 15));
            watch.Stop();
            TestContext.WriteLine($"bracket={bracket}; wall={watch.ElapsedMilliseconds}ms\n{string.Join(Environment.NewLine, plan)}");
        }
    }

    /// <summary>两管理员互撤时第二事务预先建立旧快照，角色锁等待后必须拒绝已撤权操作者或报告40001。</summary>
    [TestMethod]
    public async Task MutualRevocation_StaleActorCannotCommit()
    {
        using var f = new Fixture(false);
        try
        {
            await f.SeedAsync();
            await InsertAsync(f.Context, f.TargetAdminRelation);
            f.BeginSerializable();
            Assert.IsTrue(await f.Repository.LockAndCheckAdministratorAsync(f.Actor.Uid, default));
            using var other = f.NewSession();
            using var transaction = other.Connection.BeginTransaction(IsolationLevel.Serializable);
            other.Transaction = transaction;
            var repo = new PostgreSqlRbacManagementRepository(ContextFor(other));
            await repo.GetUserAsync(f.Target.Uid, default); // 在第一方提交前建立旧快照。
            var wait = repo.LockAndCheckAdministratorAsync(f.Target.Uid, default);
            await f.Repository.GetUserForUpdateAsync(f.Target.Uid, default);
            await f.Repository.ReplaceUserRolesAsync(f.Target.Uid, 1, [], default);
            f.End(true);
            try
            {
                Assert.IsFalse(await wait.WaitAsync(TimeSpan.FromSeconds(15)),
                    "固定旧快照中的已撤权actor不能继续获得有效管理员身份。");
            }
            catch (PostgresException error)
            {
                Assert.AreEqual("40001", error.SqlState, "只接受明确的Serializable并发失败，不吞掉其他方言/锁错误。");
            }
            finally { transaction.Rollback(); other.Transaction = null; }
        }
        finally { f.End(false); await f.CleanupAsync(); }
    }

    /// <summary>角色校验共享锁/删除更新锁或权限校验共享锁阻塞另一方写入；回滚释放后写入恢复。</summary>
    [TestMethod]
    [DataRow("role-validation")]
    [DataRow("role-deletion")]
    [DataRow("permission-validation")]
    public async Task DefinitionLocks_BlockConcurrentChangesAndRelease(string operation)
    {
        using var f = new Fixture(false);
        try
        {
            await f.SeedAsync();
            f.BeginSerializable();
            Assert.IsTrue(await f.Repository.LockAndCheckAdministratorAsync(f.Actor.Uid, default));
            if (operation == "role-validation") await f.Repository.GetActiveRolesAsync([f.Role.Uid], default);
            else if (operation == "role-deletion") await f.Repository.GetRoleForUpdateAsync(f.Role.Uid, default);
            else await f.Repository.GetActivePermissionsAsync([f.Permission.Uid], default);
            using var other = f.NewSession();
            var table = operation.StartsWith("role", StringComparison.Ordinal) ? "sys_role" : "sys_permission";
            var id = table == "sys_role" ? f.Role.Uid : f.Permission.Uid;
            var pid = await other.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid();");
            var write = other.Connection.ExecuteAsync($"UPDATE public.{table} SET is_active = FALSE WHERE uid = @Uid;",
                new { Uid = id }, commandTimeout: 15);
            try
            {
                await WaitBlockedAsync(f.Session, pid);
                Assert.IsFalse(write.IsCompleted, "共享或更新锁必须实际阻塞定义写入。");
            }
            finally { f.End(false); }
            Assert.AreEqual(1, await write.WaitAsync(TimeSpan.FromSeconds(15)));
        }
        finally { f.End(false); await f.CleanupAsync(); }
    }

    /// <summary>角色删除与分配校验争用同一目标角色；提交删除后旧快照需拒绝或40001，回滚则校验恢复。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RoleDeletion_CompetesWithAssignmentValidation(bool commitDelete)
    {
        using var f = new Fixture(false);
        try
        {
            await f.SeedAsync(); f.BeginSerializable();
            Assert.IsTrue(await f.Repository.LockAndCheckAdministratorAsync(f.Actor.Uid, default));
            await f.Repository.GetRoleForUpdateAsync(f.Role.Uid, default);
            Assert.AreEqual(0, await f.Repository.CountRoleUsersAsync(f.Role.Uid, default));
            Assert.IsTrue(await f.Repository.UpdateRoleAsync(f.Role.Uid, 1, null, null, null, true, default));
            using var other = f.NewSession();
            using var transaction = other.Connection.BeginTransaction(IsolationLevel.Serializable);
            other.Transaction = transaction;
            var repository = new PostgreSqlRbacManagementRepository(ContextFor(other));
            var pid = await other.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT pg_backend_pid();", transaction: transaction));
            var validation = repository.GetActiveRolesAsync([f.Role.Uid], default);
            try
            {
                await WaitBlockedAsync(f.Session, pid);
                Assert.IsFalse(validation.IsCompleted);
            }
            finally { f.End(commitDelete); }
            try
            {
                var roles = await validation.WaitAsync(TimeSpan.FromSeconds(15));
                Assert.HasCount(commitDelete ? 0 : 1, roles,
                    "删除已提交时不能依据旧快照继续分配已删除角色。");
            }
            catch (PostgresException error)
            {
                Assert.IsTrue(commitDelete);
                Assert.AreEqual("40001", error.SqlState);
            }
            finally { transaction.Rollback(); other.Transaction = null; }
        }
        finally { f.End(false); await f.CleanupAsync(); }
    }

    /// <summary>更新锁在提交后释放，串行跨连接读取可以观察聚合新版本；不将快照固定读当作最新读取。</summary>
    [TestMethod]
    public async Task TargetLock_CommitReleasesAndNewConnectionReadsVersion()
    {
        using var f = new Fixture(false);
        try
        {
            await f.SeedAsync(); f.BeginSerializable();
            Assert.IsTrue(await f.Repository.LockAndCheckAdministratorAsync(f.Actor.Uid, default));
            await f.Repository.GetUserForUpdateAsync(f.Target.Uid, default);
            Assert.IsTrue(await f.Repository.UpdateUserAsync(f.Target.Uid, 1, "提交昵称", null, null, null, default));
            f.End(true);
            using var next = f.NewSession();
            var row = await new PostgreSqlRbacManagementRepository(ContextFor(next)).GetUserAsync(f.Target.Uid, default);
            Assert.IsNotNull(row); Assert.AreEqual(2, row.Version); Assert.AreEqual("提交昵称", row.NickName);
        }
        finally { f.End(false); await f.CleanupAsync(); }
    }

    /// <summary>以 pg_blocking_pids 确认真实等待，不以固定sleep冒充锁竞争证据；等待有明确上限。</summary>
    private static async Task WaitBlockedAsync(DbSession observer, int pid)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT cardinality(pg_blocking_pids(@Pid)) > 0;", new { Pid = pid }, observer.Transaction,
            cancellationToken: cancellation.Token)))
            await Task.Delay(25, cancellation.Token);
    }

    /// <summary>只使用随机四类实体和精确可追溯关系，默认回滚，不创建系统角色或变更已存在权限。</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly string _connectionString = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        public readonly string Prefix = "__M10_" + Guid.NewGuid().ToString("N") + "_";
        public readonly DbSession Session;
        public readonly RepositoryBaseContext Context;
        public readonly PostgreSqlRbacManagementRepository Repository;
        public readonly SysUser Actor = new();
        public readonly SysUser Target = new();
        public readonly SysRole Role = new();
        public readonly SysPermission Permission = new();
        public readonly SysUserRole ActorRelation = new();
        public readonly SysUserRole TargetAdminRelation = new();
        public readonly SysRolePermission PermissionRelation = new();
        public Guid AdminRoleId;
        public Fixture(bool rollback = true)
        {
            Session = NewSession(); Context = ContextFor(Session); Repository = new(Context);
            Actor.UserName = Prefix + "ACTOR"; Actor.PasswordHash = "专用不可登录哈希";
            Target.UserName = Prefix + "TARGET"; Target.PasswordHash = "专用不可登录哈希";
            Role.RoleCode = Prefix + "ROLE"; Role.RoleName = "普通角色";
            Permission.PermissionKey = Prefix + "PERMISSION"; Permission.DisplayName = "随机权限";
            Permission.GroupKey = Prefix; PermissionRelation.RoleId = Role.Uid; PermissionRelation.PermissionId = Permission.Uid;
            if (rollback) BeginSerializable();
        }

        /// <summary>只读借用现存内置管理员角色，给随机账号建立临时关系；其他实体均属本夹具。</summary>
        public async Task SeedAsync()
        {
            AdminRoleId = await Session.Connection.QuerySingleAsync<Guid>(new CommandDefinition("""
                SELECT uid FROM public.sys_role WHERE role_code = 'SYSTEM_ADMIN'
                AND is_system = TRUE AND is_active = TRUE AND is_deleted = FALSE;
                """, transaction: Session.Transaction));
            ActorRelation.UserId = Actor.Uid; ActorRelation.RoleId = AdminRoleId;
            TargetAdminRelation.UserId = Target.Uid; TargetAdminRelation.RoleId = AdminRoleId;
            await InsertAsync(Context, Actor); await InsertAsync(Context, Target);
            await InsertAsync(Context, Role); await InsertAsync(Context, Permission);
            await InsertAsync(Context, ActorRelation); await InsertAsync(Context, PermissionRelation);
        }

        /// <summary>每个场景自行拥有事务，始终保持Serializable，不自动重试或降级。</summary>
        public void BeginSerializable() => Session.Transaction = Session.Connection.BeginTransaction(IsolationLevel.Serializable);

        /// <summary>事务先结束并释放、再清空引用，调用失败交由场景记录，不擅自重试。</summary>
        public void End(bool commit)
        {
            if (Session.Transaction is not { } transaction) return;
            try { if (commit) transaction.Commit(); else transaction.Rollback(); }
            finally { transaction.Dispose(); Session.Transaction = null; }
        }

        /// <summary>第二连接使用同一故障库防线；固定等待超时，不输出私有配置。</summary>
        public DbSession NewSession()
        {
            var builder = new NpgsqlConnectionStringBuilder(_connectionString) { CommandTimeout = 15 };
            return new(new PostgreSqlConnectionFactory(builder.ConnectionString));
        }

        /// <summary>提交竞争按明确聚合UID清理所有新旧随机关系，先关系后实体；不删除既有角色或审计事实。</summary>
        public async Task CleanupAsync()
        {
            using var cleanup = NewSession();
            using var transaction = cleanup.Connection.BeginTransaction();
            var userIds = new[] { Actor.Uid, Target.Uid };
            await cleanup.Connection.ExecuteAsync("DELETE FROM public.sys_user_role WHERE user_id = ANY(@Ids);",
                new { Ids = userIds }, transaction);
            await cleanup.Connection.ExecuteAsync("DELETE FROM public.sys_role_permission WHERE role_id = @Uid;",
                new { Role.Uid }, transaction);
            await cleanup.Connection.ExecuteAsync("DELETE FROM public.sys_user WHERE uid = ANY(@Ids);", new { Ids = userIds }, transaction);
            await cleanup.Connection.ExecuteAsync("DELETE FROM public.sys_role WHERE uid = @Uid;", new { Role.Uid }, transaction);
            await cleanup.Connection.ExecuteAsync("DELETE FROM public.sys_permission WHERE uid = @Uid;", new { Permission.Uid }, transaction);
            transaction.Commit();
            Assert.AreEqual(0L, await cleanup.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM public.sys_user_role WHERE user_id = ANY(@Ids);", new { Ids = userIds }));
            Assert.AreEqual(0L, await cleanup.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM public.sys_role_permission WHERE role_id = @Uid;", new { Role.Uid }));
            Assert.AreEqual(0L, await cleanup.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM public.sys_user WHERE uid = ANY(@Ids);", new { Ids = userIds }));
            Assert.AreEqual(0L, await cleanup.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM public.sys_role WHERE uid = @Uid;", new { Role.Uid }));
            Assert.AreEqual(0L, await cleanup.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM public.sys_permission WHERE uid = @Uid;", new { Permission.Uid }));
        }
        public void Dispose() { try { End(false); } finally { Session.Dispose(); } }
    }

    /// <summary>按冻结元数据和参数类型插入本场景专用数据，保留预生成Uid。</summary>
    private static async Task InsertAsync<T>(RepositoryBaseContext context, T entity) where T : BaseEntity
    {
        context.AuditStamp.StampForInsert(entity);
        var metadata = PostgreSqlEntityMetadata.GetMetadata(typeof(T));
        var table = $"{PostgreSqlNames.Quote(metadata.SchemaName)}.{PostgreSqlNames.Quote(metadata.TableName)}";
        var columns = string.Join(", ", metadata.Columns.Select(c => PostgreSqlNames.Quote(c.ColumnName)));
        var parameters = string.Join(", ", metadata.Columns.Select(c => "@" + c.ParameterName));
        Assert.AreEqual(1, await context.Session.Connection.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO {table} ({columns}) VALUES ({parameters});", PostgreSqlParameters.Entity(entity, metadata), context.Session.Transaction)));
    }

    /// <summary>固定测试审计身份，不冒用真实管理员；管理员身份来自随机账号关系。</summary>
    private static RepositoryBaseContext ContextFor(DbSession session)
    {
        var identity = new Identity(null);
        return new(session, NullLoggerFactory.Instance, new AuditStampService(identity), identity);
    }

    /// <summary>固定夹具审计身份；登录/me 场景可指定专用账号。</summary>
    private sealed class Identity(Guid? id) : ICurrentUser
    {
        public Guid? UserId => id;
        public string UserName => "RBAC实库夹具";
        public bool IsAuthenticated => id.HasValue;
    }

}

