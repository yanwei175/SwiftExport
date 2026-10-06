using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.MasterData;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>仅在显式隔离测试库中验证执行资格的事务内角色权限重验。</summary>
[TestClass]
[DoNotParallelize]
[TestCategory("PostgreSqlExecutionEligibilityIntegration")]
public sealed class ExecutionEligibilityPermissionSqlTests
{
    /// <summary>普通角色获键后可执行，撤权、停用及停用账号被拒，内置管理员仍可执行。</summary>
    [TestMethod]
    public async Task ActiveRolePermissionAndSystemAdmin_AreRecheckedInsideTransaction()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_RBAC_TESTS") != "1")
            Assert.Inconclusive("未显式启用独立 PG RBAC 库测试。");
        var connection = Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RBAC_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("未指定专用隔离 PG 测试库连接。");
        var target = new NpgsqlConnectionStringBuilder(connection);
        Assert.AreEqual("192.168.3.12", target.Host);
        Assert.AreEqual(5432, target.Port);
        Assert.AreEqual("dv_next_pg_rbac_tests_20261005", target.Database, "禁止使用运行库或备份库。");
        target.SearchPath = "public";
        // 使用现有结构，仅本事务的四个随机新记录参与写入；权限定义和管理员角色只读。
        using var session = new DbSession(new PostgreSqlConnectionFactory(target.ConnectionString));
        var before = await AuthorizationSnapshotAsync(session.Connection);
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.Serializable);
        session.Transaction = transaction;
        try
        {
            var permissionId = await session.Connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "SELECT uid FROM public.sys_permission WHERE permission_key='MasterData.Eligibility.Manage' AND is_active=TRUE AND is_deleted=FALSE;",
                transaction: transaction));
            Assert.AreNotEqual(Guid.Empty, permissionId);
            var userId = Guid.NewGuid();
            var roleId = Guid.NewGuid();
            var relationId = Guid.NewGuid();
            var rolePermissionId = Guid.NewGuid();
            await session.Connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO public.sys_user(uid,user_name,password_hash,credential_version,is_active)
                VALUES(@UserId,@UserName,'test-only-unusable-hash',1,TRUE);
                INSERT INTO public.sys_role(uid,role_code,role_name,is_active,is_system)
                VALUES(@RoleId,@RoleCode,'执行资格测试',TRUE,FALSE);
                INSERT INTO public.sys_user_role(uid,user_id,role_id) VALUES(@RelationId,@UserId,@RoleId);
                INSERT INTO public.sys_role_permission(uid,role_id,permission_id) VALUES(@RolePermissionId,@RoleId,@PermissionId);
                """, new { UserId = userId, UserName = $"eligibility-{userId:N}", RoleId = roleId,
                    RoleCode = $"ELIG_{roleId:N}", RelationId = relationId, RolePermissionId = rolePermissionId,
                    PermissionId = permissionId }, transaction));
            var repository = new PostgreSqlExecutionEligibilityRepository(new RepositoryBaseContext(session,
                NullLoggerFactory.Instance, null!, null!));
            Assert.IsTrue(await repository.HasExecutionEligibilityPermissionAsync(userId));

            await session.Connection.ExecuteAsync(new CommandDefinition(
                "UPDATE public.sys_role_permission SET is_deleted=TRUE WHERE uid=@Id;", new { Id = rolePermissionId }, transaction));
            Assert.IsFalse(await repository.HasExecutionEligibilityPermissionAsync(userId));

            var adminRoleId = await session.Connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "SELECT uid FROM public.sys_role WHERE role_code='SYSTEM_ADMIN' AND is_active=TRUE AND is_deleted=FALSE;",
                transaction: transaction));
            Assert.AreNotEqual(Guid.Empty, adminRoleId);
            await session.Connection.ExecuteAsync(new CommandDefinition(
                "UPDATE public.sys_user_role SET role_id=@RoleId WHERE uid=@Id;",
                new { RoleId = adminRoleId, Id = relationId }, transaction));
            Assert.IsTrue(await repository.HasExecutionEligibilityPermissionAsync(userId));
            await session.Connection.ExecuteAsync(new CommandDefinition(
                "UPDATE public.sys_user SET is_active=FALSE WHERE uid=@UserId;", new { UserId = userId }, transaction));
            Assert.IsFalse(await repository.HasExecutionEligibilityPermissionAsync(userId));
        }
        finally
        {
            transaction.Rollback();
            session.Transaction = null;
        }
        Assert.AreEqual(before, await AuthorizationSnapshotAsync(session.Connection), "事务回滚后四表全部原记录及数量必须恢复。");
    }

    /// <summary>读取四个固定授权表的完整行及数量摘要，不生成结构、不排除旧数据或修改任何记录。</summary>
    private static async Task<string> AuthorizationSnapshotAsync(IDbConnection connection)
    {
        var snapshots = new List<string>();
        foreach (var table in new[] { "sys_user", "sys_role", "sys_user_role", "sys_role_permission" })
        {
            var snapshot = await connection.ExecuteScalarAsync<string>($"""
                SELECT count(*)::text||':'||md5(COALESCE(string_agg(to_jsonb(t)::text,E'\n' ORDER BY uid),''))
                FROM public.{table} t;
                """);
            Assert.IsNotNull(snapshot, "完整保护摘要不得为空。");
            snapshots.Add(snapshot);
        }
        return string.Join("|", snapshots);
    }
}
