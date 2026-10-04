using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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

/// <summary>使用真实 PostgreSQL 会话验证权限聚合和撤销后读取；全部专用数据在调用方事务内创建并回滚。</summary>
[TestClass]
public sealed class PostgreSqlAuthMigrationIntegrationTests
{
    /// <summary>不存在的用户返回空；无角色的活动账号保留身份，并按空昵称规则返回用户摘要。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlIntegration")]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("测试昵称 ")]
    public Task MissingUserAndUnassignedAccount_KeepIdentityAndNicknameFallback(string? nickName) =>
        InRollbackAsync(async context =>
        {
            var repository = new PostgreSqlUserPermissionRepository(context);
            var user = TestUser();
            user.NickName = nickName;
            Assert.IsNull(await repository.GetPermissionFactsAsync(user.Uid));
            await InsertAsync(context, user);

            var facts = await repository.GetPermissionFactsAsync(user.Uid);
            Assert.IsNotNull(facts);
            Assert.AreEqual(user.Uid, facts.UserId);
            Assert.IsTrue(facts.IsActive);
            Assert.IsFalse(facts.IsAdmin);
            Assert.HasCount(0, facts.RoleNames);
            Assert.HasCount(0, facts.RoleCodes);
            Assert.HasCount(0, facts.PermissionKeys);
            var summary = (await repository.GetActiveUsersAsync()).Single(item => item.Uid == user.Uid);
            var expectedName = nickName is null || nickName.TrimEnd(' ').Length == 0 ? user.UserName : nickName;
            Assert.AreEqual(user.UserName, summary.UserName);
            Assert.AreEqual(expectedName, summary.NickName);
            Assert.IsFalse(summary.IsAdmin);
        });

    /// <summary>停用或软删除的专用账号不贡献身份事实，也不出现在活动用户摘要中。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlIntegration")]
    [DataRow(false, false)]
    [DataRow(true, true)]
    public Task InvalidAccount_HasNoFactsOrActiveSummary(bool isActive, bool isDeleted) =>
        InRollbackAsync(async context =>
        {
            var user = TestUser();
            user.IsActive = isActive;
            user.IsDeleted = isDeleted;
            await InsertAsync(context, user);
            var repository = new PostgreSqlUserPermissionRepository(context);

            Assert.IsNull(await repository.GetPermissionFactsAsync(user.Uid));
            Assert.IsFalse((await repository.GetActiveUsersAsync()).Any(item => item.Uid == user.Uid));
        });

    /// <summary>真实联表聚合对多角色重复名称及共享权限去重，并按数据库顺序映射三个文本数组。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlIntegration")]
    public Task MultipleRoles_MapSortedDistinctPermissionUnion() =>
        InRollbackAsync(async context =>
        {
            var user = TestUser();
            var prefix = Prefix();
            var roleC = TestRole(prefix + "C", prefix + "A");
            var roleB = TestRole(prefix + "B", prefix + "B");
            var roleA = TestRole(prefix + "A", prefix + "A");
            var permissionB = TestPermission(prefix + "B");
            var permissionA = TestPermission(prefix + "A");
            await InsertAsync(context, user);
            await InsertAsync(context, roleC);
            await InsertAsync(context, roleB);
            await InsertAsync(context, roleA);
            await InsertAsync(context, permissionB);
            await InsertAsync(context, permissionA);
            foreach (var role in new[] { roleC, roleB, roleA })
            {
                await InsertAsync(context, new SysUserRole { Uid = Guid.NewGuid(), UserId = user.Uid, RoleId = role.Uid });
                await InsertAsync(context, new SysRolePermission { Uid = Guid.NewGuid(), RoleId = role.Uid, PermissionId = permissionB.Uid });
            }
            await InsertAsync(context, new SysRolePermission { Uid = Guid.NewGuid(), RoleId = roleA.Uid, PermissionId = permissionA.Uid });

            var facts = await new PostgreSqlUserPermissionRepository(context).GetPermissionFactsAsync(user.Uid);

            Assert.IsNotNull(facts);
            Assert.IsFalse(facts.IsAdmin);
            CollectionAssert.AreEqual(new[] { prefix + "A", prefix + "B" }, facts.RoleNames.ToArray());
            CollectionAssert.AreEqual(new[] { prefix + "A", prefix + "B", prefix + "C" }, facts.RoleCodes.ToArray());
            CollectionAssert.AreEqual(new[] { prefix + "A", prefix + "B" }, facts.PermissionKeys.ToArray());
        });

    /// <summary>逐层验证删除的授权关系、停用或删除的角色与权限定义均不贡献有效授权。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlIntegration")]
    [DataRow("DeletedUserRole")]
    [DataRow("InactiveRole")]
    [DataRow("DeletedRole")]
    [DataRow("DeletedRolePermission")]
    [DataRow("InactivePermission")]
    [DataRow("DeletedPermission")]
    public Task InvalidAuthorizationSource_IsExcluded(string source) =>
        InRollbackAsync(async context =>
        {
            var user = TestUser();
            var prefix = Prefix();
            var role = TestRole(prefix + "ROLE", "Admin");
            var permission = TestPermission(prefix + "PERMISSION");
            var userRole = new SysUserRole { Uid = Guid.NewGuid(), UserId = user.Uid, RoleId = role.Uid };
            var rolePermission = new SysRolePermission { Uid = Guid.NewGuid(), RoleId = role.Uid, PermissionId = permission.Uid };
            userRole.IsDeleted = source == "DeletedUserRole";
            role.IsActive = source != "InactiveRole";
            role.IsDeleted = source == "DeletedRole";
            rolePermission.IsDeleted = source == "DeletedRolePermission";
            permission.IsActive = source != "InactivePermission";
            permission.IsDeleted = source == "DeletedPermission";
            await InsertAsync(context, user);
            await InsertAsync(context, role);
            await InsertAsync(context, permission);
            await InsertAsync(context, userRole);
            await InsertAsync(context, rolePermission);

            var facts = await new PostgreSqlUserPermissionRepository(context).GetPermissionFactsAsync(user.Uid);

            Assert.IsNotNull(facts);
            Assert.IsFalse(facts.IsAdmin);
            Assert.HasCount(0, facts.PermissionKeys);
            if (source is "DeletedUserRole" or "InactiveRole" or "DeletedRole")
            {
                Assert.HasCount(0, facts.RoleCodes);
                Assert.HasCount(0, facts.RoleNames);
            }
            else
            {
                CollectionAssert.AreEqual(new[] { role.RoleCode }, facts.RoleCodes.ToArray());
                CollectionAssert.AreEqual(new[] { "Admin" }, facts.RoleNames.ToArray());
            }
        });

    /// <summary>只读借用现存活动 SYSTEM_ADMIN 角色；专用用户的临时关联撤销后管理员投影立即消失。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlIntegration")]
    public Task SystemAdministrator_TemporaryRelationIsRecognizedAndRevoked() =>
        InRollbackAsync(async context =>
        {
            var adminRoleId = await context.Session.Connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition("""
                SELECT uid FROM public.sys_role
                WHERE role_code = @RoleCode AND is_active = TRUE AND is_deleted = FALSE;
                """, new { RoleCode = "SYSTEM_ADMIN" }, context.Session.Transaction));
            Assert.IsNotNull(adminRoleId, "目标库缺少现存活动 SYSTEM_ADMIN 角色，管理员事实验收不能成立。");
            var user = TestUser();
            var relation = new SysUserRole { Uid = Guid.NewGuid(), UserId = user.Uid, RoleId = adminRoleId.Value };
            await InsertAsync(context, user);
            await InsertAsync(context, relation);
            var repository = new PostgreSqlUserPermissionRepository(context);

            var before = await repository.GetPermissionFactsAsync(user.Uid);
            Assert.IsNotNull(before);
            Assert.IsTrue(before.IsAdmin);
            CollectionAssert.AreEqual(new[] { "SYSTEM_ADMIN" }, before.RoleCodes.ToArray());
            Assert.IsTrue((await repository.GetActiveUsersAsync()).Single(item => item.Uid == user.Uid).IsAdmin);

            await RevokeUserRoleAsync(context, relation.Uid);

            var after = await repository.GetPermissionFactsAsync(user.Uid);
            Assert.IsNotNull(after);
            Assert.IsFalse(after.IsAdmin);
            Assert.HasCount(0, after.RoleCodes);
            Assert.HasCount(0, after.PermissionKeys);
            Assert.IsFalse((await repository.GetActiveUsersAsync()).Single(item => item.Uid == user.Uid).IsAdmin);
        });

    /// <summary>普通授权在同一真实事务内撤销后重查，权限先消失而角色保留，撤销角色后身份仍保留。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlIntegration")]
    public Task Revocation_RequeryReturnsCurrentFactsWithoutCachedAuthorization() =>
        InRollbackAsync(async context =>
        {
            var user = TestUser();
            var prefix = Prefix();
            var role = TestRole(prefix + "ROLE", prefix + "ROLE");
            var permission = TestPermission(prefix + "PERMISSION");
            var userRole = new SysUserRole { Uid = Guid.NewGuid(), UserId = user.Uid, RoleId = role.Uid };
            var rolePermission = new SysRolePermission { Uid = Guid.NewGuid(), RoleId = role.Uid, PermissionId = permission.Uid };
            await InsertAsync(context, user);
            await InsertAsync(context, role);
            await InsertAsync(context, permission);
            await InsertAsync(context, userRole);
            await InsertAsync(context, rolePermission);
            var repository = new PostgreSqlUserPermissionRepository(context);
            var before = await repository.GetPermissionFactsAsync(user.Uid);
            Assert.IsNotNull(before);
            CollectionAssert.AreEqual(new[] { permission.PermissionKey }, before.PermissionKeys.ToArray());

            Assert.AreEqual(1, await context.Session.Connection.ExecuteAsync(new CommandDefinition("""
                UPDATE public.sys_role_permission SET is_deleted = TRUE, version = version + 1
                WHERE uid = @Uid AND is_deleted = FALSE;
                """, new { rolePermission.Uid }, context.Session.Transaction)));
            var withoutPermission = await repository.GetPermissionFactsAsync(user.Uid);
            Assert.IsNotNull(withoutPermission);
            Assert.HasCount(0, withoutPermission.PermissionKeys);
            CollectionAssert.AreEqual(new[] { role.RoleCode }, withoutPermission.RoleCodes.ToArray());

            await RevokeUserRoleAsync(context, userRole.Uid);
            var withoutRole = await repository.GetPermissionFactsAsync(user.Uid);
            Assert.IsNotNull(withoutRole);
            Assert.IsTrue(withoutRole.IsActive);
            Assert.HasCount(0, withoutRole.RoleNames);
            Assert.HasCount(0, withoutRole.RoleCodes);
            Assert.HasCount(0, withoutRole.PermissionKeys);
        });

    /// <summary>仅通过主控环境校验取得连接；finally 回滚本场景所有写入，绝不提交或删除已有数据。</summary>
    private static async Task InRollbackAsync(Func<RepositoryBaseContext, Task> scenario)
    {
        var connectionString = PostgreSqlIntegrationEnvironment.GetConnectionString();
        using var session = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        using var transaction = session.Connection.BeginTransaction();
        session.Transaction = transaction;
        var user = new TestCurrentUser();
        var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        try
        {
            await scenario(context);
        }
        finally
        {
            try { transaction.Rollback(); }
            finally { session.Transaction = null; }
        }
    }

    /// <summary>复用实体映射和参数绑定插入专用数据，保留预生成 Uid 与删除标志以构造负向场景。</summary>
    private static async Task InsertAsync<T>(RepositoryBaseContext context, T entity) where T : BaseEntity
    {
        // 通用 Add 会重新生成 Uid 并重置 IsDeleted，不能用于已关联的专用记录和删除状态夹具。
        context.AuditStamp.StampForInsert(entity);
        var metadata = PostgreSqlEntityMetadata.GetMetadata(typeof(T));
        var table = $"{PostgreSqlNames.Quote(metadata.SchemaName)}.{PostgreSqlNames.Quote(metadata.TableName)}";
        var columns = string.Join(", ", metadata.Columns.Select(column => PostgreSqlNames.Quote(column.ColumnName)));
        var values = string.Join(", ", metadata.Columns.Select(column => "@" + column.ParameterName));
        var rows = await context.Session.Connection.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO {table} ({columns}) VALUES ({values});",
            PostgreSqlParameters.Entity(entity, metadata), context.Session.Transaction));
        Assert.AreEqual(1, rows, $"专用 {typeof(T).Name} 数据应只插入一行。");
    }

    /// <summary>只按本场景记录的随机关联 Uid 撤销临时关系，并断言命中一行。</summary>
    private static async Task RevokeUserRoleAsync(RepositoryBaseContext context, Guid relationId) =>
        Assert.AreEqual(1, await context.Session.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE public.sys_user_role SET is_deleted = TRUE, version = version + 1
            WHERE uid = @Uid AND is_deleted = FALSE;
            """, new { Uid = relationId }, context.Session.Transaction)));

    /// <summary>生成随机前缀，避免与既有账号、角色和权限定义的唯一值碰撞。</summary>
    private static string Prefix() => "__pgtest_" + Guid.NewGuid().ToString("N") + "_";

    /// <summary>创建不可登录的专用账号，固定非 BCrypt 标记仅用于权限查询，不涉及真实密码材料。</summary>
    private static SysUser TestUser() => new()
    {
        Uid = Guid.NewGuid(), UserName = Prefix(), PasswordHash = "__pgtest_unusable_password_hash__",
        CredentialVersion = 1, IsActive = true
    };

    /// <summary>创建普通专用角色，不触碰现存系统角色。</summary>
    private static SysRole TestRole(string code, string name) => new()
    { Uid = Guid.NewGuid(), RoleCode = code, RoleName = name, IsActive = true, IsSystem = false };

    /// <summary>创建专用权限定义，只用于仓储原始事实读取，不扩展代码权限目录。</summary>
    private static SysPermission TestPermission(string key) => new()
    { Uid = Guid.NewGuid(), PermissionKey = key, DisplayName = key, GroupKey = Prefix(), IsActive = true };

    /// <summary>仅给专用数据提供测试审计名称，不模拟任何现存登录用户。</summary>
    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_auth_integration__";
        public bool IsAuthenticated => false;
    }
}
