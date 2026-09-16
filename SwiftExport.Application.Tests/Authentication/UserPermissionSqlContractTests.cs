using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.DatabaseSchema;
using SwiftExport.Core.Entities.Auth;

namespace SwiftExport.Application.Tests.Authentication;

/// <summary>静态验证模块关系元数据和采购消费者的单点接线。</summary>
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

    /// <summary>用户模块权限必须用活动唯一索引保证幂等并声明逻辑用户关系。</summary>
    [TestMethod]
    public void ModulePermissionMetadataIsUniqueAndTraceable()
    {
        var index = typeof(SysUserModulePermission).GetCustomAttributes(typeof(DbIndexAttribute), true).Cast<DbIndexAttribute>()
            .Single(item => item.Name == "UX_SysUserModulePermissions_User_Key");
        Assert.IsTrue(index.IsUnique);
        Assert.AreEqual("[IsDeleted] = 0", index.Filter);
        Assert.IsNotEmpty(typeof(SysUserModulePermission).GetCustomAttributes(typeof(LogicalReferenceAttribute), true));
    }

    /// <summary>模块替换使用调用方事务、软删除、审计和最终 Admin/目标重验。</summary>
    [TestMethod]
    public void RepositoryReplacementKeepsTransactionAuditAndSecurityGuards()
    {
        var source = Read("SwiftExport.Infrastructure", "Repositories", "Auth", "UserPermissionRepository.cs");
        StringAssert.Contains(source, "context.Session.Transaction ?? throw");
        StringAssert.Contains(source, "context.AuditStamp.StampForInsert");
        StringAssert.Contains(source, "SET IsDeleted=1,Version=Version+1");
        StringAssert.Contains(source, "ar.RoleName=N'Admin'");
        StringAssert.Contains(source, "tr.RoleName=N'Admin'");
        StringAssert.Contains(source, "WITH(UPDLOCK,HOLDLOCK)");
    }

    /// <summary>采购与仓库候选不再各自读取角色 SQL，统一消费权限投影服务。</summary>
    [TestMethod]
    public void ProcurementConsumersUseSingleProjectionPoint()
    {
        var transaction = Read("SwiftExport.Application", "Services", "Procurement", "OrderManagement", "ProcurementOrderTransaction.cs");
        var warehouse = Read("SwiftExport.Application", "Services", "Inventory", "WarehouseCandidateQueryService.cs");
        var procurementRepository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "ProcurementOrderRepository.cs");
        var warehouseRepository = Read("SwiftExport.Infrastructure", "Repositories", "Inventory", "WarehouseCandidateRepository.cs");
        StringAssert.Contains(transaction, "IUserPermissionService permissions");
        StringAssert.Contains(warehouse, "IUserPermissionService permissions");
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
