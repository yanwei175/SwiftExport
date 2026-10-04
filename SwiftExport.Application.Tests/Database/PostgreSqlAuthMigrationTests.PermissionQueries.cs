using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Auth.Permissions;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用无网络连接夹具验证权限查询的 Dapper 映射及命令边界；不代替实库比较和撤权并发验收。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>没有活动用户结果时返回空，且只执行一个读取命令，不自行建立事务。</summary>
    [TestMethod]
    public async Task AuthPermissionFacts_MissingUserReturnsNullWithoutStartingTransaction()
    {
        using var fixture = new Fixture();
        fixture.Session.Transaction!.Dispose();
        fixture.Session.Transaction = null;
        AuthPermissionColumns(fixture.Connection.Rows);
        var repository = new PostgreSqlUserPermissionRepository(AuthPermissionContext(fixture));
        var userId = Guid.NewGuid();

        Assert.IsNull(await repository.GetPermissionFactsAsync(userId));

        Assert.HasCount(1, fixture.Connection.Commands);
        Assert.AreEqual(userId, fixture.Connection.Values["UserId"]);
        Assert.IsNull(fixture.Connection.LastTransaction);
        Assert.IsNull(fixture.Session.Transaction);
        Assert.AreEqual(0, fixture.Connection.Commits);
        Assert.AreEqual(0, fixture.Connection.Rollbacks);
        Assert.AreEqual(1, fixture.Connection.TransactionDisposals);
    }

    /// <summary>没有角色的活动用户仍得到有效身份及三个空集合，文本数组通过真实 Dapper 映射。</summary>
    [TestMethod]
    public async Task AuthPermissionFacts_UserWithoutRolesKeepsIdentity()
    {
        using var fixture = new Fixture();
        AuthPermissionColumns(fixture.Connection.Rows);
        var userId = Guid.NewGuid();
        fixture.Connection.Rows.Rows.Add(userId, true, false, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        var repository = new PostgreSqlUserPermissionRepository(AuthPermissionContext(fixture));

        var result = await repository.GetPermissionFactsAsync(userId);

        Assert.IsNotNull(result);
        Assert.AreEqual(userId, result.UserId);
        Assert.IsTrue(result.IsActive);
        Assert.IsFalse(result.IsAdmin);
        Assert.HasCount(0, result.RoleNames);
        Assert.HasCount(0, result.RoleCodes);
        Assert.HasCount(0, result.PermissionKeys);
        StringAssert.Contains(fixture.Connection.Text, "LEFT JOIN");
        StringAssert.Contains(fixture.Connection.Text, "ARRAY[]::text[]");
    }

    /// <summary>多角色结果保留数据库集合次序；SQL 对联表产生的重复事实分别去重和排序。</summary>
    [TestMethod]
    public async Task AuthPermissionFacts_MapsMultipleRolesAndSortedPermissionUnion()
    {
        using var fixture = new Fixture();
        AuthPermissionColumns(fixture.Connection.Rows);
        var userId = Guid.NewGuid();
        string[] names = ["Admin", "采购"];
        string[] codes = ["BUYER", "READER"];
        string[] keys = ["Procurement.Order.Read", "Sales.Order.Read"];
        fixture.Connection.Rows.Rows.Add(userId, true, false, names, codes, keys);
        var repository = new PostgreSqlUserPermissionRepository(AuthPermissionContext(fixture));

        var result = await repository.GetPermissionFactsAsync(userId);

        Assert.IsNotNull(result);
        Assert.IsFalse(result.IsAdmin);
        CollectionAssert.AreEqual(names, result.RoleNames.ToArray());
        CollectionAssert.AreEqual(codes, result.RoleCodes.ToArray());
        CollectionAssert.AreEqual(keys, result.PermissionKeys.ToArray());
        foreach (var column in new[] { "r.role_name", "r.role_code", "p.permission_key" })
            StringAssert.Contains(fixture.Connection.Text, $"array_agg(DISTINCT {column}::text ORDER BY {column}::text)");
        StringAssert.Contains(fixture.Connection.Text, "NULLIF(rtrim(r.role_name, ' '), '') IS NOT NULL");
        Assert.HasCount(1, fixture.Connection.Commands);
    }

    /// <summary>管理员布尔投影由稳定编码判断并独立映射，不因显示名称或缺少显式权限而改变。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task AuthPermissionFacts_MapsAdministratorFromStableRoleCode(bool isAdmin)
    {
        using var fixture = new Fixture();
        AuthPermissionColumns(fixture.Connection.Rows);
        var userId = Guid.NewGuid();
        fixture.Connection.Rows.Rows.Add(userId, true, isAdmin,
            new[] { isAdmin ? "改过的显示名" : "Admin" },
            new[] { isAdmin ? "SYSTEM_ADMIN" : "ORDINARY_ROLE" }, Array.Empty<string>());
        var repository = new PostgreSqlUserPermissionRepository(AuthPermissionContext(fixture));

        var result = await repository.GetPermissionFactsAsync(userId);

        Assert.IsNotNull(result);
        Assert.AreEqual(isAdmin, result.IsAdmin);
        Assert.HasCount(0, result.PermissionKeys);
        StringAssert.Contains(fixture.Connection.Text, "COALESCE(bool_or(r.role_code = 'SYSTEM_ADMIN'), FALSE)");
        Assert.IsFalse(fixture.Connection.Text.Contains("r.role_name =", StringComparison.Ordinal));
    }

    /// <summary>单 SELECT 显式约束五表活动事实及集合空值，结果别名匹配 CLR，不读取密码或获取更新锁。</summary>
    [TestMethod]
    public async Task AuthPermissionFacts_SqlFiltersEveryAuthorizationSourceInOneSnapshot()
    {
        using var fixture = new Fixture();
        AuthPermissionColumns(fixture.Connection.Rows);
        var repository = new PostgreSqlUserPermissionRepository(AuthPermissionContext(fixture));

        await repository.GetPermissionFactsAsync(Guid.NewGuid());

        var sql = fixture.Connection.Text;
        foreach (var table in new[] { "sys_user", "sys_user_role", "sys_role", "sys_role_permission", "sys_permission" })
            StringAssert.Contains(sql, $"\"public\".\"{table}\"");
        foreach (var alias in new[] { "u", "ur", "r", "rp", "p" })
            StringAssert.Contains(sql, $"COALESCE({alias}.is_deleted, FALSE) = FALSE");
        foreach (var alias in new[] { "u", "r", "p" })
            StringAssert.Contains(sql, $"{alias}.is_active = TRUE");
        foreach (var property in new[] { "UserId", "IsActive", "IsAdmin", "RoleNames", "RoleCodes", "PermissionKeys" })
            StringAssert.Contains(sql, $"AS \"{property}\"");
        StringAssert.Contains(sql, "FILTER (WHERE r.uid IS NOT NULL)");
        StringAssert.Contains(sql, "FILTER (WHERE p.uid IS NOT NULL)");
        StringAssert.Contains(sql, "GROUP BY u.uid;");
        Assert.AreEqual(1, sql.Count(character => character == ';'));
        Assert.IsFalse(sql.Contains("FOR UPDATE", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("HOLDLOCK", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("password_hash", StringComparison.Ordinal));
    }

    /// <summary>活动用户摘要映射昵称和管理员标记；SQL 保留空昵称回退、活动过滤和稳定排序。</summary>
    [TestMethod]
    public async Task AuthActiveUsers_MapsSummaryAndKeepsNicknameFallbackSql()
    {
        using var fixture = new Fixture();
        var rows = fixture.Connection.Rows;
        rows.Columns.Add("Uid", typeof(Guid));
        rows.Columns.Add("UserName", typeof(string));
        rows.Columns.Add("NickName", typeof(string));
        rows.Columns.Add("IsAdmin", typeof(bool));
        var adminId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        rows.Rows.Add(adminId, "admin", "管理员", true);
        rows.Rows.Add(userId, "buyer", "buyer", false);
        var repository = new PostgreSqlUserPermissionRepository(AuthPermissionContext(fixture));

        var result = await repository.GetActiveUsersAsync();

        Assert.HasCount(2, result);
        Assert.AreEqual(adminId, result[0].Uid);
        Assert.AreEqual("admin", result[0].UserName);
        Assert.AreEqual("管理员", result[0].NickName);
        Assert.IsTrue(result[0].IsAdmin);
        Assert.AreEqual(userId, result[1].Uid);
        Assert.AreEqual("buyer", result[1].NickName);
        Assert.IsFalse(result[1].IsAdmin);
        var sql = fixture.Connection.Text;
        StringAssert.Contains(sql, "CASE WHEN NULLIF(rtrim(u.nick_name, ' '), '') IS NULL THEN u.user_name ELSE u.nick_name END AS \"NickName\"");
        StringAssert.Contains(sql, "ORDER BY u.user_name, u.uid");
        foreach (var alias in new[] { "u", "ur", "r" })
            StringAssert.Contains(sql, $"COALESCE({alias}.is_deleted, FALSE) = FALSE");
        StringAssert.Contains(sql, "u.is_active = TRUE");
        StringAssert.Contains(sql, "r.is_active = TRUE");
        StringAssert.Contains(sql, "r.role_code = 'SYSTEM_ADMIN'");
        Assert.IsFalse(sql.Contains("password_hash", StringComparison.Ordinal));
        Assert.HasCount(1, fixture.Connection.Commands);
    }

    /// <summary>两个读取端口均使用调用方事务，不提交、回滚、释放或替换该事务。</summary>
    [TestMethod]
    public async Task AuthPermissionQueries_PreserveCallerTransaction()
    {
        using var fixture = new Fixture();
        var transaction = fixture.Session.Transaction;
        var repository = new PostgreSqlUserPermissionRepository(AuthPermissionContext(fixture));

        await repository.GetPermissionFactsAsync(Guid.NewGuid());
        await repository.GetActiveUsersAsync();

        Assert.HasCount(2, fixture.Connection.Commands);
        foreach (var command in fixture.Connection.Commands)
            Assert.AreSame(transaction, command.Transaction);
        Assert.AreSame(transaction, fixture.Session.Transaction);
        Assert.AreEqual(0, fixture.Connection.Commits);
        Assert.AreEqual(0, fixture.Connection.Rollbacks);
        Assert.AreEqual(0, fixture.Connection.TransactionDisposals);
    }

    /// <summary>预取消令牌经 Dapper 传到 ADO 异步读取，两个端口都在执行前取消并保留调用方事务。</summary>
    [TestMethod]
    public async Task AuthPermissionQueries_ForwardPreCanceledToken()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var transaction = fixture.Session.Transaction;
        var repository = new PostgreSqlUserPermissionRepository(AuthPermissionContext(fixture));

        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetPermissionFactsAsync(Guid.NewGuid(), cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetActiveUsersAsync(cancellation.Token));

        Assert.HasCount(0, fixture.Connection.Commands);
        Assert.AreSame(transaction, fixture.Session.Transaction);
        Assert.AreEqual(0, fixture.Connection.Commits);
        Assert.AreEqual(0, fixture.Connection.Rollbacks);
        Assert.AreEqual(0, fixture.Connection.TransactionDisposals);
    }

    /// <summary>提供 PostgreSQL 查询返回的 CLR 别名和文本数组类型，不模拟 SQL 引擎过滤或聚合。</summary>
    private static void AuthPermissionColumns(DataTable rows)
    {
        rows.Columns.Add("UserId", typeof(Guid));
        rows.Columns.Add("IsActive", typeof(bool));
        rows.Columns.Add("IsAdmin", typeof(bool));
        rows.Columns.Add("RoleNames", typeof(string[]));
        rows.Columns.Add("RoleCodes", typeof(string[]));
        rows.Columns.Add("PermissionKeys", typeof(string[]));
    }

    /// <summary>为权限仓储提供既有夹具会话和审计身份，不复制连接或事务假件。</summary>
    private static RepositoryBaseContext AuthPermissionContext(Fixture fixture)
    {
        var user = new User();
        return new(fixture.Session, NullLoggerFactory.Instance, new AuditStampService(user), user);
    }
}
