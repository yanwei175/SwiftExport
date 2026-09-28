using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.DatabaseSchema;
using SwiftExport.Core.Entities.Auth;

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
        var source = Read("SwiftExport.Infrastructure", "Repositories", "Auth", "Permissions", "RbacManagementRepository.cs");
        var service = Read("SwiftExport.Application", "Services", "Auth", "Permissions", "RbacManagementService.cs");
        StringAssert.Contains(source, "WITH(UPDLOCK,HOLDLOCK)");
        StringAssert.Contains(source, "SYSTEM_ADMIN");
        StringAssert.Contains(source, "SysUser_Roles");
        StringAssert.Contains(source, "IsDeleted=1");
        StringAssert.Contains(service, "ReplaceUserRolesAsync");
        StringAssert.Contains(service, "AuditAsync(\"ReplaceUserRoles\"");
    }

    /// <summary>采购与仓库候选不再各自读取角色 SQL，统一消费权限投影服务。</summary>
    [TestMethod]
    public void ProcurementConsumersUseSingleProjectionPoint()
    {
        var reads = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseOrders", "Support", "ProcurementOrderReadSupport.cs");
        var warehouse = Read("SwiftExport.Application", "Services", "Inventory", "Warehouses", "WarehouseCandidateQueryService.cs");
        var procurementRepository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrders", "Draft", "ProcurementOrderDocumentRepository.cs");
        var warehouseRepository = Read("SwiftExport.Infrastructure", "Repositories", "Inventory", "Warehouses", "WarehouseCandidateRepository.cs");
        StringAssert.Contains(reads, "IPermissionEvaluationService permissions");
        StringAssert.Contains(warehouse, "IPermissionEvaluationService permissions");
        Assert.IsFalse(procurementRepository.Contains("SysUser_Roles", StringComparison.Ordinal));
        Assert.IsFalse(warehouseRepository.Contains("SysUser_Roles", StringComparison.Ordinal));
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
