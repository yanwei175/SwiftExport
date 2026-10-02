using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Core.Interfaces.Auth.Permissions;
using SwiftExport.Core.Models.Auth;

namespace SwiftExport.Application.Tests.Authentication;

/// <summary>验证统一权限投影只使用有效角色事实和代码目录，不从名称或旧模块键推断授权。</summary>
[TestClass]
public sealed class RbacPermissionEvaluationTests
{
    /// <summary>多角色权限合并去重，旧模块键不会展开采购写权限。</summary>
    [TestMethod]
    public async Task OrdinaryRoles_UnionWithoutLegacyExpansion()
    {
        var userId = Guid.NewGuid();
        var repository = new FactsRepository(new UserPermissionFacts
        {
            UserId = userId, IsActive = true, RoleNames = ["采购只读", "供应商只读"],
            RoleCodes = ["BUYER_VIEW", "SUPPLIER_VIEW"],
            PermissionKeys = ["Procurement.Order.Read", "Suppliers.Supplier.View", "Suppliers.Supplier.View"],
        });
        var service = new PermissionEvaluationService(repository);

        var projection = await service.GetProjectionAsync(userId);

        Assert.IsNotNull(projection);
        Assert.IsFalse(projection.IsAdmin);
        CollectionAssert.AreEquivalent(new[] { "Procurement.Order.Read", "Suppliers.Supplier.View" }, projection.Permissions.ToArray());
        CollectionAssert.AreEquivalent(new[] { "BUYER_VIEW", "SUPPLIER_VIEW" }, projection.RoleCodes.ToArray());
        Assert.IsFalse(await service.HasPermissionAsync(userId, "Procurement.Order.Create"));
        Assert.IsFalse(await service.HasPermissionAsync(userId, "Module.Procurement.Access"));
    }

    /// <summary>普通角色即使显示名为 Admin，也不能获得内置管理员身份。</summary>
    [TestMethod]
    public async Task DisplayNameAdmin_DoesNotGrantAdministrator()
    {
        var userId = Guid.NewGuid();
        var service = new PermissionEvaluationService(new FactsRepository(new UserPermissionFacts
        {
            UserId = userId, IsActive = true, RoleNames = ["Admin"], RoleCodes = ["ORDINARY_ROLE"]
        }));

        var projection = await service.GetProjectionAsync(userId);

        Assert.IsNotNull(projection);
        Assert.IsFalse(projection.IsAdmin);
        Assert.AreEqual(0, projection.Permissions.Count);
        Assert.IsFalse(await service.HasPermissionAsync(userId, "System.Users.Manage"));
    }

    /// <summary>普通角色获显式授权后可维护地址读取配置，历史权限键继续兼容已有授权。</summary>
    [TestMethod]
    public async Task AssignedRole_CanManageExcelReadProfiles()
    {
        var userId = Guid.NewGuid();
        var service = new PermissionEvaluationService(new FactsRepository(new UserPermissionFacts
        {
            UserId = userId, IsActive = true, RoleCodes = ["IMPORT_CONFIG_ADMIN"],
            PermissionKeys = [PermissionKeys.SystemExcelFieldMappingManage]
        }));

        Assert.IsTrue(await service.HasPermissionAsync(userId, PermissionKeys.SystemExcelFieldMappingManage));
        Assert.IsFalse(await service.HasPermissionAsync(userId, PermissionKeys.SystemUsersManage));
    }

    /// <summary>内置管理员自动拥有完整代码目录，未知键仍被拒绝。</summary>
    [TestMethod]
    public async Task SystemAdministrator_HasCatalogOnly()
    {
        var userId = Guid.NewGuid();
        var service = new PermissionEvaluationService(new FactsRepository(new UserPermissionFacts
        {
            UserId = userId, IsActive = true, IsAdmin = true,
            RoleNames = ["改过的显示名"], RoleCodes = [SystemRoleCodes.SystemAdmin]
        }));

        var projection = await service.GetProjectionAsync(userId);

        Assert.IsNotNull(projection);
        Assert.IsTrue(projection.IsAdmin);
        Assert.AreEqual(PermissionCatalog.All.Count, projection.Permissions.Count);
        Assert.IsTrue(await service.HasPermissionAsync(userId, "System.Users.Manage"));
        Assert.IsTrue(await service.HasPermissionAsync(userId, PermissionKeys.SystemExcelFieldMappingManage));
        Assert.IsFalse(await service.HasPermissionAsync(userId, "Unknown.Permission"));
    }

    /// <summary>停用账号不产生权限投影，保持旧 Token 的下一次请求无法继续授权。</summary>
    [TestMethod]
    public async Task InactiveUser_HasNoProjection()
    {
        var userId = Guid.NewGuid();
        var service = new PermissionEvaluationService(new FactsRepository(new UserPermissionFacts
        {
            UserId = userId, IsActive = false, IsAdmin = true, RoleCodes = [SystemRoleCodes.SystemAdmin]
        }));

        Assert.IsNull(await service.GetProjectionAsync(userId));
        Assert.IsFalse(await service.HasPermissionAsync(userId, "System.Users.Manage"));
    }

    /// <summary>只为投影提供指定数据库事实，不执行授权写操作。</summary>
    private sealed class FactsRepository(UserPermissionFacts facts) : IUserPermissionRepository
    {
        public Task<UserPermissionFacts?> GetPermissionFactsAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<UserPermissionFacts?>(userId == facts.UserId ? facts : null);

        public Task<IReadOnlyList<UserSummaryDto>> GetActiveUsersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserSummaryDto>>([]);

    }
}
