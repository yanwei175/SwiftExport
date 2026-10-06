using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.DatabaseSchema;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Infrastructure.Repositories.Auth.Permissions;

namespace SwiftExport.Application.Tests.Authentication;

/// <summary>静态验证 RBAC 关系元数据和采购消费者的单点接线。</summary>
[TestClass]
public sealed class UserPermissionSqlContractTests
{
    /// <summary>凭据版本正值约束必须由实体元数据声明。</summary>
    [TestMethod]
    public void CredentialVersionConstraintComesFromEntityMetadata()
    {
        var check = typeof(SysUser).GetCustomAttributes(typeof(DbCheckAttribute), true).Cast<DbCheckAttribute>()
            .Single(item => item.Name == "CK_SysUser_CredentialVersion");
        StringAssert.Contains(check.Expression, "[CredentialVersion] >= 1");
    }

    /// <summary>角色权限必须用活动唯一索引保证幂等并声明逻辑权限关系。</summary>
    [TestMethod]
    public void RolePermissionMetadataIsUniqueAndTraceable()
    {
        var index = typeof(SysRolePermission).GetCustomAttributes(typeof(DbIndexAttribute), true).Cast<DbIndexAttribute>()
            .Single(item => item.Name == "UX_SysRolePermissions_Role_Permission");
        Assert.IsTrue(index.IsUnique);
        Assert.AreEqual("[IsDeleted] = 0", index.Filter);
        Assert.IsNotEmpty(typeof(SysRolePermission).GetCustomAttributes(typeof(LogicalReferenceAttribute), true));
    }

    /// <summary>角色整组替换使用聚合版本、关系软删除和锁定的管理员重验。</summary>
    [TestMethod]
    public void RepositoryReplacementKeepsTransactionAuditAndSecurityGuards()
    {
        var source = Read("SwiftExport.Infrastructure", "Repositories", "Auth", "Permissions", "PostgreSqlRbacManagementRepository.cs");
        var service = Read("SwiftExport.Application", "Services", "Auth", "Permissions", "RbacManagementService.cs");
        // 静态检查当前 PG 管理锁与父事务守卫，不把它当真实并发验收。
        StringAssert.Contains(source, "LOCK TABLE {Roles} IN SHARE ROW EXCLUSIVE MODE");
        StringAssert.Contains(source, "FOR UPDATE OF r");
        StringAssert.Contains(source, "FOR UPDATE OF actor");
        StringAssert.Contains(source, "RequireTransaction();");
        StringAssert.Contains(source, "SYSTEM_ADMIN");
        var userRoles = (string)typeof(PostgreSqlRbacManagementRepository).GetField("UserRoles", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        Assert.AreEqual("\"public\".\"sys_user_role\"", userRoles);
        StringAssert.Contains(source, "UPDATE {UserRoles} SET is_deleted = TRUE");
        StringAssert.Contains(service, "ReplaceUserRolesAsync");
        // 迁移后的审计调用沿用本次命令身份，仍必须记录用户角色整组替换操作。
        StringAssert.Contains(service, "AuditAsync(commandId, \"ReplaceUserRoles\"");
    }

    /// <summary>采购与仓库候选不再各自读取角色 SQL，统一消费权限投影服务。</summary>
    [TestMethod]
    public void ProcurementConsumersUseSingleProjectionPoint()
    {
        var reads = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseOrders", "Support", "ProcurementOrderReadSupport.cs");
        var warehouse = Read("SwiftExport.Application", "Services", "Inventory", "Warehouses", "WarehouseCandidateQueryService.cs");
        var procurementRepository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrders", "Draft", "PostgreSqlProcurementOrderDocumentRepository.cs");
        var warehouseRepository = Read("SwiftExport.Infrastructure", "Repositories", "Inventory", "Warehouses", "PostgreSqlWarehouseCandidateRepository.cs");
        StringAssert.Contains(reads, "IPermissionEvaluationService permissions");
        StringAssert.Contains(warehouse, "IPermissionEvaluationService permissions");
        Assert.IsFalse(procurementRepository.Contains("SysUser_Roles", StringComparison.Ordinal));
        Assert.IsFalse(warehouseRepository.Contains("SysUser_Roles", StringComparison.Ordinal));
        Assert.IsFalse(procurementRepository.Contains("sys_user_role", StringComparison.Ordinal));
        Assert.IsFalse(warehouseRepository.Contains("sys_user_role", StringComparison.Ordinal));
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                    return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("找不到仓库根目录。");
    }
}
