using System.Data;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.MasterData;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>仅在显式隔离测试库中验证执行资格的事务内角色权限重验。</summary>
[TestClass]
public sealed class ExecutionEligibilityPermissionSqlTests
{
    /// <summary>普通角色获键后可执行，撤权、停用及停用账号被拒，内置管理员仍可执行。</summary>
    [TestMethod]
    public async Task ActiveRolePermissionAndSystemAdmin_AreRecheckedInsideTransaction()
    {
        var connection = Environment.GetEnvironmentVariable("SWIFTEXPORT_PERMISSION_SQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("未指定专用隔离测试库连接，跳过 SQL 行为测试。");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Default"] = connection }).Build();
        using var session = new DbSession(config);
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.Serializable);
        session.Transaction = transaction;
        try
        {
            var permissionId = await session.Connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "SELECT Uid FROM dbo.SysPermissionDefinitions WHERE PermissionKey=N'MasterData.Eligibility.Manage' AND IsActive=1 AND IsDeleted=0;",
                transaction: transaction));
            Assert.AreNotEqual(Guid.Empty, permissionId);
            var userId = Guid.NewGuid();
            var roleId = Guid.NewGuid();
            var relationId = Guid.NewGuid();
            var rolePermissionId = Guid.NewGuid();
            await session.Connection.ExecuteAsync(new CommandDefinition("""
                INSERT dbo.SysUser(Uid,UserName,PasswordHash,CredentialVersion,IsActive)
                VALUES(@UserId,@UserName,N'test-only-unusable-hash',1,1);
                INSERT dbo.SysRoles(Uid,RoleCode,RoleName,IsActive,IsSystem)
                VALUES(@RoleId,@RoleCode,N'执行资格测试',1,0);
                INSERT dbo.SysUser_Roles(Uid,UserId,RoleId) VALUES(@RelationId,@UserId,@RoleId);
                INSERT dbo.SysRole_Permissions(Uid,RoleId,PermissionId) VALUES(@RolePermissionId,@RoleId,@PermissionId);
                """, new { UserId = userId, UserName = $"eligibility-{userId:N}", RoleId = roleId,
                    RoleCode = $"ELIG_{roleId:N}", RelationId = relationId, RolePermissionId = rolePermissionId,
                    PermissionId = permissionId }, transaction));
            var repository = new ExecutionEligibilityRepository(new RepositoryBaseContext(session,
                NullLoggerFactory.Instance, null!, null!));
            Assert.IsTrue(await repository.HasExecutionEligibilityPermissionAsync(userId));

            await session.Connection.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.SysRole_Permissions SET IsDeleted=1 WHERE Uid=@Id;", new { Id = rolePermissionId }, transaction));
            Assert.IsFalse(await repository.HasExecutionEligibilityPermissionAsync(userId));

            var adminRoleId = await session.Connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "SELECT Uid FROM dbo.SysRoles WHERE RoleCode=N'SYSTEM_ADMIN' AND IsActive=1 AND IsDeleted=0;",
                transaction: transaction));
            Assert.AreNotEqual(Guid.Empty, adminRoleId);
            await session.Connection.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.SysUser_Roles SET RoleId=@RoleId WHERE Uid=@Id;",
                new { RoleId = adminRoleId, Id = relationId }, transaction));
            Assert.IsTrue(await repository.HasExecutionEligibilityPermissionAsync(userId));
            await session.Connection.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.SysUser SET IsActive=0 WHERE Uid=@UserId;", new { UserId = userId }, transaction));
            Assert.IsFalse(await repository.HasExecutionEligibilityPermissionAsync(userId));
        }
        finally
        {
            transaction.Rollback();
            session.Transaction = null;
        }
    }
}
