using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.Authentication;

/// <summary>静态验证模块关系 SQL、迁移预览开关和采购消费者的单点接线。</summary>
[TestClass]
public sealed class UserPermissionSqlContractTests
{
    /// <summary>凭据版本迁移延迟解析条件新增列，并在失败时回滚事务。</summary>
    [TestMethod]
    public void CredentialVersionMigrationDefersNewColumnReferencesAndRollsBackOnFailure()
    {
        var source = Read("SwiftExport.Infrastructure", "Migrations", "022_SysUser_CredentialVersion.sql");
        StringAssert.Contains(source, "EXEC sys.sp_executesql N'");
        StringAssert.Contains(source, "UPDATE dbo.SysUser");
        StringAssert.Contains(source, "ALTER TABLE dbo.SysUser ALTER COLUMN CredentialVersion int NOT NULL");
        StringAssert.Contains(source, "ADD CONSTRAINT DF_SysUser_CredentialVersion");
        StringAssert.Contains(source, "ADD CONSTRAINT CK_SysUser_CredentialVersion_Positive");
        StringAssert.Contains(source, "BEGIN TRY");
        StringAssert.Contains(source, "IF XACT_STATE() <> 0");
        StringAssert.Contains(source, "ROLLBACK TRANSACTION");
    }

    /// <summary>迁移默认预览、限制八个模块并用活动唯一索引保证幂等。</summary>
    [TestMethod]
    public void MigrationIsPreviewFirstIdempotentAndAudited()
    {
        var source = Read("SwiftExport.Infrastructure", "Migrations", "023_SysUser_ModulePermissions.sql");
        StringAssert.Contains(source, "DECLARE @ApplyLegacyProcurementMigration bit = 0");
        StringAssert.Contains(source, "UX_SysUser_ModulePermissions_ActiveUserKey");
        StringAssert.Contains(source, "WHERE IsDeleted=0");
        StringAssert.Contains(source, "NOT EXISTS");
        StringAssert.Contains(source, "N'Migration023'");
        StringAssert.Contains(source, "ur.RoleId");
        StringAssert.Contains(source, "AlreadyMigrated");
        Assert.IsFalse(source.Contains("System.Settings.Manage", StringComparison.Ordinal));
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
